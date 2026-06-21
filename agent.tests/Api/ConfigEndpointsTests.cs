using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
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

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private Task<LoreApiHarness> StartAsync(InMemoryCredentialStore store, IProviderReloader? reloader = null) =>
        LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton(new LoreConfig(_path, store));
                services.AddSingleton<IProviderReloader>(reloader ?? new CountingReloader());
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
