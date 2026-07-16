using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Memory;

namespace Lore.Agent.Tests.Memory;

public sealed class MemorydClientTests : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }
    }

    private (MemorydClient Client, StubHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, (HttpStatusCode, string)> responder,
        int getAllPageSize = 500)
    {
        var handler = new StubHttpMessageHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:7842/") };
        _clients.Add(http);
        return (new MemorydClient(http, getAllPageSize), handler);
    }

    private static int QueryOffset(Uri uri)
    {
        foreach (string part in uri.Query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("offset=", StringComparison.Ordinal))
            {
                return int.Parse(part["offset=".Length..], CultureInfo.InvariantCulture);
            }
        }

        return 0;
    }

    private static JsonElement Body(StubHttpMessageHandler handler) =>
        JsonDocument.Parse(handler.LastBody ?? "{}").RootElement;

    [Fact]
    public async Task RememberAsync_posts_to_memories_and_parses_results()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[{"id":"m1","memory":"User likes tea.","event":"ADD"}]}"""));

        IReadOnlyList<AddedMemory> result = await client.RememberAsync(
            "User likes tea.",
            "u1",
            new Dictionary<string, object?> { ["source"] = "screen" });

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/memories", handler.LastUri!.AbsolutePath);
        AddedMemory only = Assert.Single(result);
        Assert.Equal("m1", only.Id);
        Assert.Equal("ADD", only.Event);

        JsonElement body = Body(handler);
        Assert.Equal("User likes tea.", body.GetProperty("text").GetString());
        Assert.Equal("u1", body.GetProperty("user_id").GetString());
        Assert.Equal("screen", body.GetProperty("metadata").GetProperty("source").GetString());
    }

    [Fact]
    public async Task SearchAsync_posts_query_and_parses_records()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK,
                """{"results":[{"id":"m1","memory":"Lives in Austin.","score":0.91,"created_at":"2026-06-12"}]}"""));

        IReadOnlyList<MemoryRecord> result = await client.SearchAsync("where do they live", "u1", 5);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/memories/search", handler.LastUri!.AbsolutePath);
        MemoryRecord record = Assert.Single(result);
        Assert.Equal(0.91, record.Score);
        Assert.Equal("2026-06-12", record.CreatedAt);

        JsonElement body = Body(handler);
        Assert.Equal("where do they live", body.GetProperty("query").GetString());
        Assert.Equal("u1", body.GetProperty("user_id").GetString());
        Assert.Equal(5, body.GetProperty("limit").GetInt32());
    }

    [Fact]
    public async Task GetRecentAsync_requests_memories_with_user_and_limit()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[]}"""));

        await client.GetRecentAsync("u1", 7);

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/memories", handler.LastUri!.AbsolutePath);
        Assert.Contains("user_id=u1", handler.LastUri.Query, StringComparison.Ordinal);
        Assert.Contains("limit=7", handler.LastUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAllAsync_requests_all_memories()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[{"id":"m1","memory":"x"}]}"""));

        IReadOnlyList<MemoryRecord> result = await client.GetAllAsync("u1");

        Assert.Single(result);
        Assert.Equal("/memories", handler.LastUri!.AbsolutePath);
        Assert.Contains("user_id=u1", handler.LastUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAllAsync_pages_until_a_short_page()
    {
        var requestedOffsets = new List<int>();
        (MemorydClient client, _) = Build(
            request =>
            {
                int offset = QueryOffset(request.RequestUri!);
                requestedOffsets.Add(offset);
                int count = offset < 4 ? 2 : 1; // pages of size 2, 2, then a partial 1
                string items = string.Join(
                    ",",
                    Enumerable.Range(0, count).Select(i => $"{{\"id\":\"m{offset + i}\",\"memory\":\"x\"}}"));
                return (HttpStatusCode.OK, $"{{\"results\":[{items}]}}");
            },
            getAllPageSize: 2);

        IReadOnlyList<MemoryRecord> all = await client.GetAllAsync("u1");

        Assert.Equal(5, all.Count); // 2 + 2 + 1 across three pages
        Assert.Equal([0, 2, 4], requestedOffsets); // stopped after the short page
    }

    [Fact]
    public async Task ListResponse_missing_results_yields_empty_not_null()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.OK, "{}"));
        Assert.Empty(await client.GetRecentAsync("u1"));
    }

    [Fact]
    public async Task GetAsync_parses_record_on_200()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"id":"m1","memory":"User likes tea."}"""));

        MemoryRecord? record = await client.GetAsync("m1");

        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Equal("/memories/m1", handler.LastUri!.AbsolutePath);
        Assert.Equal("User likes tea.", record!.Memory);
    }

    [Fact]
    public async Task GetAsync_returns_null_on_404()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.NotFound, """{"detail":"nope"}"""));
        Assert.Null(await client.GetAsync("missing"));
    }

    [Fact]
    public async Task UpdateAsync_patches_and_returns_record()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"id":"m1","memory":"User likes coffee."}"""));

        MemoryRecord? record = await client.UpdateAsync("m1", "User likes coffee.");

        Assert.Equal(HttpMethod.Patch, handler.LastMethod);
        Assert.Equal("/memories/m1", handler.LastUri!.AbsolutePath);
        Assert.Equal("User likes coffee.", record!.Memory);
        Assert.Equal("User likes coffee.", Body(handler).GetProperty("text").GetString());
    }

    [Fact]
    public async Task UpdateAsync_returns_null_on_404()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.NotFound, "{}"));
        Assert.Null(await client.UpdateAsync("missing", "x"));
    }

    [Fact]
    public async Task UpdateAsync_sends_metadata_patch_and_omits_absent_text()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"id":"m1","memory":"unchanged"}"""));

        await client.UpdateAsync(
            "m1", metadataPatch: new Dictionary<string, object?> { ["status"] = "archived" });

        JsonElement body = Body(handler);
        Assert.Equal("archived", body.GetProperty("metadata").GetProperty("status").GetString());
        Assert.False(body.TryGetProperty("text", out _)); // null text omitted, never sent
    }

    [Fact]
    public async Task UpdateAsync_rejects_patch_with_nothing_to_change()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAsync("m1"));
    }

    [Fact]
    public async Task UpdateAsync_rejects_empty_text()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.OK, "{}"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateAsync("m1", ""));
    }

    [Fact]
    public async Task SearchAsync_sends_filters_in_body()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[]}"""));

        await client.SearchAsync(
            "travel ideas", "u1", 5, MemoryFilters.ActiveUnexpired(1_800_000_000));

        JsonElement filters = Body(handler).GetProperty("filters");
        Assert.Equal("active", filters.GetProperty("status").GetString());
        Assert.Equal(1_800_000_000, filters.GetProperty("expires_at").GetProperty("gt").GetInt64());
    }

    [Fact]
    public async Task ListAsync_sends_filters_as_json_query_parameter()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[]}"""));

        await client.ListAsync("u1", 20, 0, new Dictionary<string, object?> { ["kind"] = "state" });

        Assert.Equal("/memories", handler.LastUri!.AbsolutePath);
        string decoded = Uri.UnescapeDataString(handler.LastUri.Query);
        Assert.Contains("\"kind\":\"state\"", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_serializes_typed_metadata_and_returns_single_result()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"results":[{"id":"m9","memory":"I visited France.","event":"ADD"}]}"""));

        var metadata = new MemoryMetadata(
            MemoryKinds.Experience, MemoryStatuses.Active, 0.9,
            MemoryMetadata.FarFutureUnixSeconds, 1_799_000_000, MemoryUpdateReasons.Promoted);
        AddedMemory added = await client.StoreAsync("I visited France.", metadata);

        Assert.Equal("m9", added.Id);
        JsonElement meta = Body(handler).GetProperty("metadata");
        Assert.Equal("experience", meta.GetProperty("kind").GetString());
        Assert.Equal(MemoryMetadata.FarFutureUnixSeconds, meta.GetProperty("expires_at").GetInt64());
        Assert.Equal(1, meta.GetProperty("v").GetInt32());
    }

    [Fact]
    public async Task DeleteAsync_returns_true_on_success_and_false_on_404()
    {
        (MemorydClient ok, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"status":"ok"}"""));
        Assert.True(await ok.DeleteAsync("m1"));
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal("/memories/m1", handler.LastUri!.AbsolutePath);

        (MemorydClient missing, _) = Build(_ => (HttpStatusCode.NotFound, "{}"));
        Assert.False(await missing.DeleteAsync("gone"));
    }

    [Fact]
    public async Task ConfigureAsync_posts_provider_config_in_snake_case()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Build(
            _ => (HttpStatusCode.OK, """{"status":"ok","collection_name":"lore"}"""));

        var config = new MemoryConfig(
            new MemoryProviderConfig("openai", "gpt-4o", new Uri("http://proxy.local"), "sk-test"),
            "/data/lore",
            Embedder: null,
            CollectionName: "lore",
            UserId: "u1");

        await client.ConfigureAsync(config);

        Assert.Equal("/config", handler.LastUri!.AbsolutePath);
        JsonElement body = Body(handler);
        JsonElement provider = body.GetProperty("provider");
        Assert.Equal("openai", provider.GetProperty("type").GetString());
        Assert.Equal("http://proxy.local", provider.GetProperty("base_url").GetString());
        Assert.Equal("sk-test", provider.GetProperty("api_key").GetString());
        Assert.Equal("/data/lore", body.GetProperty("data_dir").GetString());
        Assert.Equal("u1", body.GetProperty("user_id").GetString());
        // null embedder is omitted, not sent as null
        Assert.False(body.TryGetProperty("embedder", out _));
    }

    [Fact]
    public async Task Unexpected_status_throws_MemorydException_with_details()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.InternalServerError, "engine exploded"));

        MemorydException ex = await Assert.ThrowsAsync<MemorydException>(
            () => client.RememberAsync("anything"));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal("remember", ex.Operation);
        Assert.Contains("engine exploded", ex.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RememberAsync_rejects_empty_observation()
    {
        (MemorydClient client, _) = Build(_ => (HttpStatusCode.OK, """{"results":[]}"""));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RememberAsync(string.Empty));
    }
}
