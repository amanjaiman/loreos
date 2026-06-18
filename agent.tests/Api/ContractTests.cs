using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Import;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Lore.Agent.Storage;
using Lore.Agent.Tests.Config;
using Lore.Agent.Tests.Memory;
using Lore.Agent.Tests.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 acceptance criteria 6 &amp; 7: the full API assembled by
/// <see cref="ApiHost.MapLoreApi"/> is exercised end-to-end against a seeded store, and the
/// generated <c>/openapi.json</c> is accurate and versioned. This is the contract suite the
/// surfaces (006/007/010) build against — if a route or its shape changes, it fails here.</summary>
public sealed class ContractTests : IAsyncLifetime, IDisposable
{
    private readonly string _configPath = Path.Combine(Path.GetTempPath(), $"lore-contract-cfg-{Guid.NewGuid():N}.json");
    private readonly string _logPath = Path.Combine(Path.GetTempPath(), $"lore-contract-log-{Guid.NewGuid():N}.log");
    private readonly FakeMemoryService _memory = new();
    private readonly ActivityStore _activity = new(":memory:");

    private LoreApiHarness _harness = null!;
    private string _seededId = null!;

    public async Task InitializeAsync()
    {
        _seededId = _memory.Seed("I prefer concise answers");
        await _activity.LogActivityAsync(new ActivityLogEntry(
            DateTimeOffset.UtcNow, "code.exe", "main.cs", ActivityDecision.Captured, "novel", "wrote a test", "ide"));
        await _activity.LogRawCaptureAsync(new RawCaptureEntry(
            DateTimeOffset.UtcNow, "code.exe", "main.cs", "uia", "code", "some captured text"));
        await File.WriteAllLinesAsync(_logPath, ["log line a", "log line b"]);

        _harness = await LoreApiHarness.StartAsync(ConfigureServices, Map);
    }

    [Theory]
    [InlineData("GET", "/health", HttpStatusCode.OK)]
    [InlineData("GET", "/memories", HttpStatusCode.OK)]
    [InlineData("GET", "/recent", HttpStatusCode.OK)]
    [InlineData("GET", "/activity", HttpStatusCode.OK)]
    [InlineData("GET", "/config", HttpStatusCode.OK)]
    [InlineData("GET", "/system/status", HttpStatusCode.OK)]
    [InlineData("GET", "/system/log", HttpStatusCode.OK)]
    [InlineData("GET", "/export/json", HttpStatusCode.OK)]
    [InlineData("GET", "/export/markdown", HttpStatusCode.OK)]
    [InlineData("POST", "/import", HttpStatusCode.BadRequest)] // 009: empty body -> needs a path
    public async Task Every_simple_route_answers_as_documented(string method, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));
        HttpResponseMessage response = await _harness.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Memory_crud_round_trips_through_the_assembled_host()
    {
        // get one (seeded)
        HttpResponseMessage get = await _harness.Client.GetAsync(new Uri($"/memories/{_seededId}", UriKind.Relative));
        get.EnsureSuccessStatusCode();

        // search
        HttpResponseMessage search = await _harness.Client.PostAsJsonAsync(
            new Uri("/memories/search", UriKind.Relative), new { query = "concise" });
        search.EnsureSuccessStatusCode();

        // add -> 201
        HttpResponseMessage add = await _harness.Client.PostAsJsonAsync(
            new Uri("/memories", UriKind.Relative), new { text = "I work in the mornings" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        // update
        HttpResponseMessage update = await _harness.Client.PatchAsJsonAsync(
            new Uri($"/memories/{_seededId}", UriKind.Relative), new { text = "I prefer very concise answers" });
        update.EnsureSuccessStatusCode();

        // delete -> 204
        HttpResponseMessage delete = await _harness.Client.DeleteAsync(new Uri($"/memories/{_seededId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Providers_test_and_config_patch_work_through_the_host()
    {
        HttpResponseMessage test = await _harness.Client.PostAsJsonAsync(
            new Uri("/providers/test", UriKind.Relative), new { model = "claude-haiku-4-5" });
        test.EnsureSuccessStatusCode();

        HttpResponseMessage patch = await _harness.Client.PatchAsJsonAsync(
            new Uri("/config", UriKind.Relative), new { provider = new { model = "gpt-4o-mini" } });
        patch.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task OpenApi_document_is_served_versioned_and_lists_the_routes()
    {
        HttpResponseMessage response = await _harness.Client.GetAsync(new Uri("/openapi.json", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = doc.RootElement;

        // versioned: info.version pins to the API contract version
        Assert.Equal(ApiHost.ApiVersion, root.GetProperty("info").GetProperty("version").GetString());

        // accurate: the document lists the real routes surfaces build against
        JsonElement paths = root.GetProperty("paths");
        foreach (string expected in new[]
                 {
                     "/memories", "/memories/search", "/memories/{id}", "/recent", "/activity",
                     "/config", "/providers/test", "/import", "/import/{id}", "/system/status",
                     "/system/log", "/system/data", "/export/json", "/export/markdown",
                 })
        {
            Assert.True(paths.TryGetProperty(expected, out _), $"OpenAPI is missing path {expected}");
        }
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null)
        {
            await _harness.DisposeAsync();
        }
    }

    public void Dispose()
    {
        _activity.Dispose();
        File.Delete(_configPath);
        File.Delete(_logPath);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IMemoryService>(_memory);
        services.AddSingleton(_activity);
        services.AddSingleton(_ => new LoreConfig(_configPath, new InMemoryCredentialStore()));
        services.AddSingleton(new ProviderOptions { Type = "anthropic", Model = "claude-haiku-4-5", ApiKeyRef = "lore/provider" });
        services.AddSingleton(new ProviderTester(new FakeBackendFactory(_ => StubBackend.Returns("OK"))));
        services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
        services.AddSingleton(new LogTail(_logPath));

        // The import pipeline (009): one line registers the job store + service. The contract
        // suite only pokes the route shapes (an empty POST -> 400), so the scoped import service —
        // and its capture-filter dependency — is never resolved here.
        services.AddDocumentImport();

        ApiHost.AddOpenApi(services);
    }

    private static void Map(WebApplication app)
    {
        ApiHost.UseOpenApi(app);
        app.MapLoreApi();
    }
}
