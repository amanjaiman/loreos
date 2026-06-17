using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 acceptance criterion 4: <c>/export/json</c> and <c>/export/markdown</c>
/// return the full memory set in valid, documented formats.</summary>
public sealed class ExportEndpointsTests
{
    [Fact]
    public async Task Json_export_returns_the_full_set_with_a_count()
    {
        var store = new FakeMemoryService();
        store.Seed("I use a standing desk");
        store.Seed("My timezone is UTC-5");
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/export/json", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal(2, root.GetProperty("memories").GetArrayLength());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("exported_at").GetString()));
        Assert.Equal("I use a standing desk", root.GetProperty("memories")[0].GetProperty("memory").GetString());
    }

    [Fact]
    public async Task Markdown_export_is_markdown_listing_every_memory()
    {
        var store = new FakeMemoryService();
        store.Seed("I use a standing desk");
        store.Seed("My timezone is UTC-5");
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/export/markdown", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("# Lore memory export", body, StringComparison.Ordinal);
        Assert.Contains("- I use a standing desk", body, StringComparison.Ordinal);
        Assert.Contains("- My timezone is UTC-5", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Markdown_export_handles_an_empty_store()
    {
        await using LoreApiHarness harness = await StartAsync(new FakeMemoryService());

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/export/markdown", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("_No memories yet._", body, StringComparison.Ordinal);
    }

    private static Task<LoreApiHarness> StartAsync(FakeMemoryService store) =>
        LoreApiHarness.StartAsync(
            services => services.AddSingleton<IMemoryService>(store),
            app => app.MapExportEndpoints());
}
