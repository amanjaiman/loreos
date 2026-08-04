using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 acceptance criterion 2: every memory verb round-trips through
/// <see cref="IMemoryService"/> and returns the documented shape. Exercised end-to-end over a
/// live loopback host against a seeded fake store.</summary>
public sealed class MemoryEndpointsTests
{
    private static Task<LoreApiHarness> StartAsync(FakeMemoryService store) =>
        LoreApiHarness.StartAsync(
            services => services.AddSingleton<IMemoryService>(store),
            app => app.MapMemoryEndpoints());

    [Fact]
    public async Task List_returns_a_page_with_total_and_window()
    {
        var store = new FakeMemoryService();
        store.Seed("alpha");
        store.Seed("beta");
        store.Seed("gamma");
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/memories?limit=2&offset=1");
        JsonElement root = doc.RootElement;

        Assert.Equal(3, root.GetProperty("total").GetInt32());
        Assert.Equal(2, root.GetProperty("limit").GetInt32());
        Assert.Equal(1, root.GetProperty("offset").GetInt32());
        Assert.Equal(2, root.GetProperty("items").GetArrayLength());
        Assert.Equal("beta", root.GetProperty("items")[0].GetProperty("memory").GetString());
    }

    [Fact]
    public async Task Get_returns_the_record_or_404()
    {
        var store = new FakeMemoryService();
        string id = store.Seed("a remembered thing");
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument found = await GetJsonAsync(harness, $"/memories/{id}");
        Assert.Equal("a remembered thing", found.RootElement.GetProperty("memory").GetString());
        Assert.Equal(id, found.RootElement.GetProperty("id").GetString());

        HttpResponseMessage missing = await harness.Client.GetAsync(new Uri("/memories/nope", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Search_returns_scored_hits_and_rejects_an_empty_query()
    {
        var store = new FakeMemoryService();
        store.Seed("the quick brown fox");
        store.Seed("a lazy dog");
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            new Uri("/memories/search", UriKind.Relative), new { query = "fox" });
        response.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement results = doc.RootElement.GetProperty("results");
        Assert.Equal(1, results.GetArrayLength());
        Assert.Equal("the quick brown fox", results[0].GetProperty("memory").GetString());
        Assert.True(results[0].GetProperty("score").GetDouble() > 0);

        HttpResponseMessage empty = await harness.Client.PostAsJsonAsync(
            new Uri("/memories/search", UriKind.Relative), new { query = "" });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task Add_returns_201_with_results_and_the_memory_becomes_listable()
    {
        var store = new FakeMemoryService();
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            new Uri("/memories", UriKind.Relative), new { text = "I prefer dark mode" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement first = doc.RootElement.GetProperty("results")[0];
        Assert.Equal("I prefer dark mode", first.GetProperty("memory").GetString());
        Assert.Equal("ADD", first.GetProperty("event").GetString());

        using JsonDocument list = await GetJsonAsync(harness, "/memories");
        Assert.Equal(1, list.RootElement.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Add_rejects_a_missing_text()
    {
        var store = new FakeMemoryService();
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            new Uri("/memories", UriKind.Relative), new { user_id = "default" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_text_or_404s()
    {
        var store = new FakeMemoryService();
        string id = store.Seed("old text");
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage ok = await harness.Client.PatchAsJsonAsync(
            new Uri($"/memories/{id}", UriKind.Relative), new { text = "new text" });
        ok.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await ok.Content.ReadAsStringAsync());
        Assert.Equal("new text", doc.RootElement.GetProperty("memory").GetString());

        HttpResponseMessage missing = await harness.Client.PatchAsJsonAsync(
            new Uri("/memories/nope", UriKind.Relative), new { text = "x" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Delete_removes_once_then_404s()
    {
        var store = new FakeMemoryService();
        string id = store.Seed("ephemeral");
        await using LoreApiHarness harness = await StartAsync(store);

        HttpResponseMessage first = await harness.Client.DeleteAsync(new Uri($"/memories/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        HttpResponseMessage second = await harness.Client.DeleteAsync(new Uri($"/memories/{id}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Stats_counts_active_by_kind_with_every_kind_present()
    {
        var store = new FakeMemoryService();
        store.Seed("a", metadata: Meta("active", "preference"));
        store.Seed("b", metadata: Meta("active", "preference"));
        store.Seed("c", metadata: Meta("active", "identity"));
        store.Seed("staged one", metadata: Meta("staged", "state"));
        store.Seed("staged two", metadata: Meta("staged", "project"));
        store.Seed("gone", metadata: Meta("archived", "experience"));
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/memories/stats");
        JsonElement root = doc.RootElement;
        JsonElement active = root.GetProperty("active");

        Assert.Equal(2, active.GetProperty("preference").GetInt32());
        Assert.Equal(1, active.GetProperty("identity").GetInt32());
        // Kinds with no active memory are present as 0, so the client never sees a missing field.
        Assert.Equal(0, active.GetProperty("state").GetInt32());
        Assert.Equal(0, active.GetProperty("experience").GetInt32());
        Assert.Equal(0, active.GetProperty("project").GetInt32());
        Assert.Equal(3, root.GetProperty("total_active").GetInt32());
        Assert.Equal(2, root.GetProperty("staged").GetInt32());
        Assert.Equal(1, root.GetProperty("archived").GetInt32());
    }

    [Fact]
    public async Task Stats_on_an_empty_store_is_all_zero_not_404()
    {
        var store = new FakeMemoryService();
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument doc = await GetJsonAsync(harness, "/memories/stats");
        JsonElement root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("total_active").GetInt32());
        Assert.Equal(0, root.GetProperty("staged").GetInt32());
        Assert.Equal(0, root.GetProperty("archived").GetInt32());
        JsonElement active = root.GetProperty("active");
        foreach (string kind in new[] { "identity", "preference", "state", "experience", "project" })
        {
            Assert.Equal(0, active.GetProperty(kind).GetInt32());
        }
    }

    [Fact]
    public async Task Stats_total_active_agrees_with_the_filtered_memories_list()
    {
        var store = new FakeMemoryService();
        store.Seed("a", metadata: Meta("active", "preference"));
        store.Seed("b", metadata: Meta("active", "state"));
        store.Seed("c", metadata: Meta("staged", "project"));
        await using LoreApiHarness harness = await StartAsync(store);

        using JsonDocument stats = await GetJsonAsync(harness, "/memories/stats");
        using JsonDocument list = await GetJsonAsync(harness, "/memories?status=active&limit=500");

        Assert.Equal(
            list.RootElement.GetProperty("total").GetInt32(),
            stats.RootElement.GetProperty("total_active").GetInt32());
    }

    private static async Task<JsonDocument> GetJsonAsync(LoreApiHarness harness, string path)
    {
        HttpResponseMessage response = await harness.Client.GetAsync(new Uri(path, UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    // A v2-shaped metadata dict (the `v` key is what MemoryMetadata.From requires to parse a row).
    private static Dictionary<string, JsonElement> Meta(string status, string kind) =>
        new Dictionary<string, JsonElement>
        {
            ["v"] = JsonSerializer.SerializeToElement(1),
            ["status"] = JsonSerializer.SerializeToElement(status),
            ["kind"] = JsonSerializer.SerializeToElement(kind),
        };
}
