using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Config;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 acceptance criterion 5: config round-trips through <c>GET/PATCH /config</c>,
/// and no secret material is ever returned or persisted inline — inline keys are routed to the
/// credential store and replaced by a handle. Exercised end-to-end over a live loopback host with
/// a temp config file.</summary>
public sealed class ConfigEndpointsTests : IDisposable
{
    private const string Secret = "sk-super-secret-key-value";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lore-config-{Guid.NewGuid():N}.json");

    [Fact]
    public async Task Get_returns_the_config_without_inline_secrets()
    {
        // A legacy/handcrafted file with an inline key must never be echoed back.
        await File.WriteAllTextAsync(_path, """
            { "provider": { "type": "openai", "model": "gpt-4o", "api_key": "INLINE-LEAK", "api_key_ref": "lore/provider" } }
            """);
        var store = new InMemoryCredentialStore();
        await using LoreApiHarness harness = await StartAsync(store);

        string body = await GetStringAsync(harness, "/config");

        Assert.DoesNotContain("INLINE-LEAK", body, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key\"", body, StringComparison.Ordinal); // the field itself is gone
        using JsonDocument doc = JsonDocument.Parse(body);
        JsonElement provider = doc.RootElement.GetProperty("provider");
        Assert.Equal("gpt-4o", provider.GetProperty("model").GetString());
        Assert.Equal("lore/provider", provider.GetProperty("api_key_ref").GetString()); // handle is fine
    }

    [Fact]
    public async Task Patch_deep_merges_without_resending_the_whole_block()
    {
        await File.WriteAllTextAsync(_path, """
            { "provider": { "type": "openai", "model": "gpt-4o" }, "capture": { "dwell_threshold": "00:00:05" } }
            """);
        var store = new InMemoryCredentialStore();
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new { provider = new { model = "gpt-4o-mini" } });
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement provider = doc.RootElement.GetProperty("provider");
        Assert.Equal("gpt-4o-mini", provider.GetProperty("model").GetString());
        Assert.Equal("openai", provider.GetProperty("type").GetString()); // untouched sibling kept
        // unrelated top-level block survives the merge
        Assert.Equal("00:00:05", doc.RootElement.GetProperty("capture").GetProperty("dwell_threshold").GetString());
    }

    [Fact]
    public async Task Patch_routes_an_inline_key_to_the_store_and_never_echoes_or_persists_it()
    {
        var store = new InMemoryCredentialStore();
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative),
            new { provider = new { type = "anthropic", model = "claude-haiku-4-5", api_key = Secret } });
        response.EnsureSuccessStatusCode();

        // Response: no secret, replaced by a handle.
        string responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Secret, responseBody, StringComparison.Ordinal);
        using JsonDocument doc = JsonDocument.Parse(responseBody);
        string handle = doc.RootElement.GetProperty("provider").GetProperty("api_key_ref").GetString()!;

        // The key landed in the credential store under that handle...
        Assert.Equal(Secret, store.Read(handle));
        // ...and the persisted file holds no key material.
        string persisted = await File.ReadAllTextAsync(_path);
        Assert.DoesNotContain(Secret, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key\"", persisted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_on_a_missing_file_returns_an_empty_object()
    {
        var store = new InMemoryCredentialStore();
        await using LoreApiHarness harness = await StartAsync(store);

        string body = await GetStringAsync(harness, "/config");

        using JsonDocument doc = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
        Assert.Empty(doc.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task Patch_rejects_a_non_object_body()
    {
        var store = new InMemoryCredentialStore();
        await using LoreApiHarness harness = await StartAsync(store);

        using var content = new StringContent("\"not an object\"", System.Text.Encoding.UTF8, "application/json");
        HttpResponseMessage response = await harness.Client.PatchAsync(new Uri("/config", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_touching_the_provider_triggers_a_reload()
    {
        await File.WriteAllTextAsync(_path, """{ "provider": { "type": "openai", "model": "gpt-4o" } }""");
        var store = new InMemoryCredentialStore();
        var reloader = new CountingReloader();
        await using LoreApiHarness harness = await StartAsync(store, reloader);

        // A provider change must be applied to the live agent...
        (await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new { provider = new { model = "gpt-4o-mini" } }))
            .EnsureSuccessStatusCode();
        Assert.Equal(1, reloader.Reloads);

        // ...but an unrelated block must not pay for a needless reconfigure.
        (await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new { capture = new { enabled = false } }))
            .EnsureSuccessStatusCode();
        Assert.Equal(1, reloader.Reloads);
    }

    [Fact]
    public async Task Patch_capture_replaces_the_running_privacy_snapshot()
    {
        var store = new InMemoryCredentialStore();
        var settings = new LiveCaptureSettings(new CaptureOptions
        {
            BlocklistApps = ["1password"],
            BlocklistKeywords = ["secret"],
        });
        await using LoreApiHarness harness = await StartAsync(store, liveCapture: settings);

        (await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative),
            new { capture = new { enabled = false, blocklistApps = Array.Empty<string>(), blocklistKeywords = new[] { "salary" } } }))
            .EnsureSuccessStatusCode();

        Assert.False(settings.Enabled);
        Assert.False(settings.Blocklist.MatchesApp("1password.exe"));
        Assert.True(settings.Blocklist.MatchesKeyword("salary discussion"));
    }

    [Fact]
    public async Task Patch_capture_immediately_clears_the_visible_capture_target()
    {
        var store = new InMemoryCredentialStore();
        var settings = new LiveCaptureSettings(new CaptureOptions());
        var status = new CaptureStatusTracker();
        status.RecordCaptured("Private payroll", DateTimeOffset.UtcNow);
        await using LoreApiHarness harness = await StartAsync(store, liveCapture: settings, status: status);

        (await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative),
            new { capture = new { blocklistKeywords = new[] { "payroll" } } }))
            .EnsureSuccessStatusCode();

        Assert.Null(status.Current.WindowTitle);
    }

    // ── v2-008 R3: capture.resolved is read-only, derived, and never persisted ─────

    [Fact]
    public async Task Get_reports_the_effective_capture_values_under_resolved()
    {
        // What the Settings consequence lines are derived from. It must reflect the file, not the
        // preset name: `light` timings, but the cap this user pinned by hand.
        await File.WriteAllTextAsync(_path, """
            { "capture": { "attentiveness": "light", "episodes": { "maxObservations": 64 } } }
            """);
        // Seeded the way AddCapturePipeline seeds it: the preset first, the explicit key on top.
        CaptureOptions options = CapturePresets.Resolve("light", null, null);
        var settings = new LiveCaptureSettings(options with
        {
            Episodes = options.Episodes with { MaxObservations = 64 },
        });
        await using LoreApiHarness harness = await StartAsync(
            new InMemoryCredentialStore(), liveCapture: settings);

        using JsonDocument doc = JsonDocument.Parse(await GetStringAsync(harness, "/config"));
        JsonElement resolved = doc.RootElement.GetProperty("capture").GetProperty("resolved");

        // Writable names, writable formats — a TimeSpan string, not a number of seconds.
        Assert.Equal("00:00:05", resolved.GetProperty("pollInterval").GetString());
        Assert.Equal("00:00:40", resolved.GetProperty("recaptureInterval").GetString());
        Assert.Equal("light", resolved.GetProperty("attentiveness").GetString());
        Assert.Equal("balanced", resolved.GetProperty("detail").GetString());
        Assert.Equal(64, resolved.GetProperty("episodes").GetProperty("maxObservations").GetInt32());
        Assert.Equal(6, resolved.GetProperty("episodes").GetProperty("maxSamples").GetInt32());

        // The writable keys are still there beside it, untouched.
        Assert.Equal("light", doc.RootElement.GetProperty("capture").GetProperty("attentiveness").GetString());
    }

    [Fact]
    public async Task Patch_never_persists_resolved_even_on_a_read_modify_write()
    {
        // The realistic way it would leak: read GET /config, change one control, send the capture
        // block straight back. Persisting it would pin every preset-derived number at whatever it
        // was and the three controls would stop doing anything at all.
        var settings = new LiveCaptureSettings(new CaptureOptions());
        await using LoreApiHarness harness = await StartAsync(
            new InMemoryCredentialStore(), liveCapture: settings);

        using JsonDocument read = JsonDocument.Parse(await GetStringAsync(harness, "/config"));
        var capture = (JsonObject)JsonNode.Parse(read.RootElement.GetProperty("capture").GetRawText())!;
        Assert.True(capture.ContainsKey("resolved")); // it really is being sent back
        capture["attentiveness"] = "close";

        HttpResponseMessage response = await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new JsonObject { ["capture"] = capture });
        response.EnsureSuccessStatusCode();

        // Not in the file…
        string persisted = await File.ReadAllTextAsync(_path);
        Assert.DoesNotContain("resolved", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("00:00:02", persisted, StringComparison.Ordinal);

        // …so the preset the user just picked is what governs, not the balanced numbers that rode
        // in on the round trip.
        Assert.Equal(TimeSpan.FromSeconds(15), settings.Current.RecaptureInterval);
        Assert.Equal(80, settings.Current.Episodes.MaxObservations);

        // The response still carries the fresh resolved block, so the caller sees what its own
        // patch resolved to without another round trip.
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "00:00:15",
            doc.RootElement.GetProperty("capture").GetProperty("resolved")
                .GetProperty("recaptureInterval").GetString());
    }

    [Fact]
    public async Task A_config_file_that_already_holds_resolved_is_cleaned_up_on_the_next_write()
    {
        // Belt and braces: the strip lives at the persistence boundary, not only at the endpoint,
        // so a file written by an earlier build (or by hand) loses the block rather than keeping
        // it forever as a set of invisible overrides.
        await File.WriteAllTextAsync(_path, """
            { "capture": { "resolved": { "pollInterval": "00:00:02" }, "attentiveness": "close" } }
            """);
        await using LoreApiHarness harness = await StartAsync(new InMemoryCredentialStore());

        (await harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new { capture = new { detail = "rich" } }))
            .EnsureSuccessStatusCode();

        string persisted = await File.ReadAllTextAsync(_path);
        Assert.DoesNotContain("resolved", persisted, StringComparison.Ordinal);
        Assert.Contains("close", persisted, StringComparison.Ordinal); // the real keys survive
    }

    [Fact]
    public async Task Get_adds_nothing_when_there_is_no_capture_pipeline_to_report_on()
    {
        // A host mapping these endpoints without the capture pipeline has no effective values, and
        // inventing a capture block for it would be a lie.
        await using LoreApiHarness harness = await StartAsync(new InMemoryCredentialStore());

        using JsonDocument doc = JsonDocument.Parse(await GetStringAsync(harness, "/config"));

        Assert.Empty(doc.RootElement.EnumerateObject());
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private Task<LoreApiHarness> StartAsync(
        InMemoryCredentialStore store,
        IProviderReloader? reloader = null,
        LiveCaptureSettings? liveCapture = null,
        CaptureStatusTracker? status = null) =>
        LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton(new LoreConfig(_path, store));
                services.AddSingleton<IProviderReloader>(reloader ?? new CountingReloader());
                if (liveCapture is not null)
                {
                    services.AddSingleton(liveCapture);
                }
                if (status is not null)
                {
                    services.AddSingleton(status);
                }
            },
            app => app.MapConfigEndpoints());

    /// <summary>A no-op <see cref="IProviderReloader"/> that records how many times it was asked to
    /// reload, so a test can assert the endpoint triggers a reload only for provider changes.</summary>
    private sealed class CountingReloader : IProviderReloader
    {
        public int Reloads { get; private set; }

        public Task ReloadAsync(CancellationToken cancellationToken = default)
        {
            Reloads++;
            return Task.CompletedTask;
        }
    }

    private static async Task<string> GetStringAsync(LoreApiHarness harness, string path)
    {
        HttpResponseMessage response = await harness.Client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
