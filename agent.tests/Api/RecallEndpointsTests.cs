using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Recall;
using Lore.Agent.Tests.Recall;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

public sealed class RecallEndpointsTests
{
    private sealed class GoldenTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => GoldenCorpus.Now;
    }

    private static Task<LoreApiHarness> StartAsync() => LoreApiHarness.StartAsync(
        services =>
        {
            services.AddSingleton(new RecallOptions());
            services.AddSingleton<TimeProvider>(new GoldenTime());
            services.AddSingleton<RecallService>(sp => new RecallService(
                new GoldenMemoryService(),
                sp.GetRequiredService<RecallOptions>(),
                sp.GetRequiredService<TimeProvider>()));
        },
        app => app.MapRecallEndpoints());

    [Fact]
    public async Task Recall_returns_scored_hits()
    {
        await using LoreApiHarness harness = await StartAsync();

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/recall", new { query = "should I order takeout tonight" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement first = body.GetProperty("results").EnumerateArray().First();
        Assert.Equal("I'm recovering from a wisdom tooth extraction.", first.GetProperty("statement").GetString());
        Assert.Equal("state", first.GetProperty("kind").GetString());
        Assert.True(first.GetProperty("score").GetDouble() > 0.5);
        Assert.True(first.GetProperty("established_at").GetInt64() > 0);
    }

    [Fact]
    public async Task Recall_empty_result_is_a_200_with_no_padding()
    {
        await using LoreApiHarness harness = await StartAsync();

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/recall", new { query = "how do I cook pasta carbonara" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task Recall_validates_query_and_kinds()
    {
        await using LoreApiHarness harness = await StartAsync();

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await harness.Client.PostAsJsonAsync("/recall", new { })).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await harness.Client.PostAsJsonAsync(
                "/recall", new { query = "x", kinds = new[] { "vibe" } })).StatusCode);
    }

    [Fact]
    public async Task Recall_honors_k_and_kinds()
    {
        await using LoreApiHarness harness = await StartAsync();

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            "/recall", new { query = "tell me about myself", k = 1, kinds = new[] { "preference" } });

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement only = Assert.Single(body.GetProperty("results").EnumerateArray().ToArray());
        Assert.Equal("preference", only.GetProperty("kind").GetString());
    }
}
