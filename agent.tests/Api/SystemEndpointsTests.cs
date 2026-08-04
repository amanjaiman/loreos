using System.IO;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Api;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Config;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 acceptance criterion 3: <c>GET /system/status</c> reports agent + memoryd +
/// provider readiness and the app version. Also covers the log tail and the data reset.</summary>
public sealed class SystemEndpointsTests
{
    [Fact]
    public async Task Status_reports_ready_components_and_versions()
    {
        var provider = new ProviderOptions { Type = "anthropic", Model = "claude-haiku-4-5", ApiKeyRef = "lore/provider" };
        await using LoreApiHarness harness = await StartAsync(services =>
        {
            services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
            services.AddSingleton(provider);
        });

        using JsonDocument doc = await GetJsonAsync(harness, "/system/status");
        JsonElement root = doc.RootElement;

        Assert.Equal(ApiHost.ApiVersion, root.GetProperty("api_version").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
        JsonElement components = root.GetProperty("components");
        Assert.Equal("running", components.GetProperty("agent").GetString());
        Assert.Equal("ready", components.GetProperty("memoryd").GetString());
        Assert.Equal("ready", components.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Status_reflects_starting_memoryd_and_unconfigured_provider()
    {
        await using LoreApiHarness harness = await StartAsync(services =>
        {
            services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: false));
            services.AddSingleton(new ProviderOptions()); // empty => not resolvable
        });

        using JsonDocument doc = await GetJsonAsync(harness, "/system/status");
        JsonElement components = doc.RootElement.GetProperty("components");

        Assert.Equal("starting", components.GetProperty("memoryd").GetString());
        Assert.Equal("unconfigured", components.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task Status_surfaces_the_watched_window_when_capture_is_on()
    {
        var tracker = new CaptureStatusTracker();
        tracker.RecordCaptured(
            "Figma — Lore rebrand",
            DateTimeOffset.Parse("2026-07-31T14:12:04Z", System.Globalization.CultureInfo.InvariantCulture));
        await using LoreApiHarness harness = await StartAsync(services =>
        {
            services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
            services.AddSingleton(new ProviderOptions());
            services.AddSingleton(tracker);
        });

        using JsonDocument doc = await GetJsonAsync(harness, "/system/status");
        JsonElement capture = doc.RootElement.GetProperty("capture");

        Assert.True(capture.GetProperty("enabled").GetBoolean());
        Assert.Equal("Figma — Lore rebrand", capture.GetProperty("window_title").GetString());
        Assert.Equal(JsonValueKind.String, capture.GetProperty("observed_at").ValueKind);
    }

    [Fact]
    public async Task Status_hides_the_title_of_an_excluded_window()
    {
        var tracker = new CaptureStatusTracker();
        tracker.RecordCaptured("Figma — Lore rebrand", DateTimeOffset.UnixEpoch);
        tracker.RecordExcluded(); // the current window is now blocklisted
        await using LoreApiHarness harness = await StartAsync(services =>
        {
            services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
            services.AddSingleton(new ProviderOptions());
            services.AddSingleton(tracker);
        });

        string body = await (await harness.Client.GetAsync(new Uri("/system/status", UriKind.Relative)))
            .Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement capture = doc.RootElement.GetProperty("capture");

        Assert.True(capture.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, capture.GetProperty("window_title").ValueKind);
        Assert.Equal(JsonValueKind.Null, capture.GetProperty("observed_at").ValueKind);
        // Belt and braces: no fragment of the excluded title survives anywhere in the response.
        Assert.DoesNotContain("Figma", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_nulls_the_watched_window_when_capture_is_paused()
    {
        string configPath = Path.Combine(Path.GetTempPath(), $"lore-cap-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(configPath, /*lang=json,strict*/ "{\"capture\":{\"enabled\":false}}");
        try
        {
            var tracker = new CaptureStatusTracker();
            tracker.RecordCaptured("Private Journal", DateTimeOffset.UtcNow); // loop hasn't stopped
            await using LoreApiHarness harness = await StartAsync(services =>
            {
                services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
                services.AddSingleton(new ProviderOptions());
                services.AddSingleton(tracker);
                services.AddSingleton(new LoreConfig(configPath, new InMemoryCredentialStore()));
            });

            using JsonDocument doc = await GetJsonAsync(harness, "/system/status");
            JsonElement capture = doc.RootElement.GetProperty("capture");

            Assert.False(capture.GetProperty("enabled").GetBoolean());
            Assert.Equal(JsonValueKind.Null, capture.GetProperty("window_title").ValueKind);
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public async Task Status_omits_the_capture_block_when_capture_is_not_wired()
    {
        await using LoreApiHarness harness = await StartAsync(services =>
        {
            services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
            services.AddSingleton(new ProviderOptions());
        });

        using JsonDocument doc = await GetJsonAsync(harness, "/system/status");

        Assert.False(doc.RootElement.TryGetProperty("capture", out _));
    }

    [Fact]
    public async Task Log_returns_the_tail_of_the_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lore-log-{Guid.NewGuid():N}.log");
        await File.WriteAllLinesAsync(path, ["line 1", "line 2", "line 3", "line 4"]);
        try
        {
            await using LoreApiHarness harness = await StartAsync(services =>
                services.AddSingleton(new LogTail(path)));

            using JsonDocument doc = await GetJsonAsync(harness, "/system/log?lines=2");
            JsonElement lines = doc.RootElement.GetProperty("lines");

            Assert.Equal(2, lines.GetArrayLength());
            Assert.Equal("line 3", lines[0].GetString());
            Assert.Equal("line 4", lines[1].GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Log_on_a_missing_file_is_empty()
    {
        string path = Path.Combine(Path.GetTempPath(), $"lore-missing-{Guid.NewGuid():N}.log");
        await using LoreApiHarness harness = await StartAsync(services =>
            services.AddSingleton(new LogTail(path)));

        using JsonDocument doc = await GetJsonAsync(harness, "/system/log");

        Assert.Empty(doc.RootElement.GetProperty("lines").EnumerateArray());
    }

    [Fact]
    public async Task Data_reset_forgets_every_memory()
    {
        var store = new FakeMemoryService();
        store.Seed("first");
        store.Seed("second");
        await using LoreApiHarness harness = await StartAsync(services =>
            services.AddSingleton<IMemoryService>(store));

        using JsonDocument doc = await DeleteJsonAsync(harness, "/system/data");
        Assert.Equal(2, doc.RootElement.GetProperty("deleted").GetInt32());

        IReadOnlyList<MemoryRecord> remaining = await store.GetAllAsync();
        Assert.Empty(remaining);
    }

    private static Task<LoreApiHarness> StartAsync(Action<IServiceCollection> configureServices) =>
        LoreApiHarness.StartAsync(configureServices, app => app.MapSystemEndpoints());

    private static async Task<JsonDocument> GetJsonAsync(LoreApiHarness harness, string path)
    {
        HttpResponseMessage response = await harness.Client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> DeleteJsonAsync(LoreApiHarness harness, string path)
    {
        HttpResponseMessage response = await harness.Client.DeleteAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
