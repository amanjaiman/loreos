using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>The v2-001 app-facing surface: episodes/decisions feeds, staging curation,
/// still-true confirmation, and the capture economy.</summary>
public sealed class ActivityEndpointsTests : IDisposable
{
    /// <summary>A fixed non-UTC zone. Without this the fake would inherit the machine's, so
    /// these tests would pass on a UTC CI runner whatever the day boundary did — the whole
    /// point is that "today" follows the user's clock, not the server's.</summary>
    private static readonly TimeZoneInfo TestZone =
        TimeZoneInfo.CreateCustomTimeZone("lore-test", TimeSpan.FromHours(-5), "Test", "Test");

    /// <summary>An explicit budget so the assertion tests that the endpoint reports the
    /// CONFIGURED value, rather than restating whatever the production default happens
    /// to be (which is a runaway guard and free to change).</summary>
    private const int ConfiguredBudget = 12;

    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TestZone;
    }

    private readonly ActivityStore _activity = new(":memory:");
    private readonly FakeMemoryService _memory = new();
    private readonly FixedTime _time = new();

    public void Dispose() => _activity.Dispose();

    private Task<LoreApiHarness> StartAsync() => LoreApiHarness.StartAsync(
        services =>
        {
            services.AddSingleton(_activity);
            services.AddSingleton<IMemoryService>(_memory);
            services.AddSingleton(new LiveCaptureSettings(new CaptureOptions
            {
                Lifecycle = new LifecycleOptions { DailyBudget = ConfiguredBudget },
            }));
            services.AddSingleton<TimeProvider>(_time);
        },
        app =>
        {
            app.MapActivityEndpoints();
            app.MapMemoryEndpoints();
        });

    private static Uri U(string path) => new(path, UriKind.Relative);

    private string SeedMemory(string text, MemoryMetadata metadata) =>
        _memory.Seed(text, metadata: metadata.ToDictionary().ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value)));

    private MemoryMetadata Meta(string status, string kind = MemoryKinds.State) => new(
        kind, status, 0.6,
        _time.Now.ToUnixTimeSeconds() + 14 * 86_400, 0,
        MemoryUpdateReasons.Staged, Episodes: ["ep-1"]);

    [Fact]
    public async Task Episodes_and_decisions_feed_render_the_trail()
    {
        var episode = new Episode(
            "ep-1", _time.Now.AddMinutes(-30), _time.Now.AddMinutes(-10),
            ["browser"], ["Wisdom tooth aftercare"], ["sample text"], 5, []);
        await _activity.SaveEpisodeAsync(episode);
        await _activity.LogDecisionAsync(new DecisionEntry(
            _time.Now, "ep-1", "staged", "awaiting a second episode",
            "I'm recovering from a wisdom tooth extraction.", "state", "m1"));

        await using LoreApiHarness harness = await StartAsync();

        JsonElement episodes = await harness.Client.GetFromJsonAsync<JsonElement>("/episodes");
        JsonElement item = Assert.Single(episodes.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("ep-1", item.GetProperty("id").GetString());
        Assert.Equal(5, item.GetProperty("observation_count").GetInt32());

        JsonElement byId = await harness.Client.GetFromJsonAsync<JsonElement>("/episodes/ep-1");
        Assert.Equal("ep-1", byId.GetProperty("id").GetString());
        Assert.Equal(
            HttpStatusCode.NotFound, (await harness.Client.GetAsync(U("/episodes/nope"))).StatusCode);

        JsonElement decisions = await harness.Client.GetFromJsonAsync<JsonElement>("/decisions");
        JsonElement decision = Assert.Single(decisions.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("staged", decision.GetProperty("action").GetString());
        Assert.Equal("ep-1", decision.GetProperty("episode_id").GetString());
    }

    [Fact]
    public async Task Evidence_returns_the_supporting_episodes_in_one_call()
    {
        var episode = new Episode(
            "ep-1", _time.Now.AddMinutes(-30), _time.Now.AddMinutes(-10),
            ["Figma.exe"], ["Figma — Lore rebrand"], ["sample text"], 12, []);
        await _activity.SaveEpisodeAsync(episode);
        string id = SeedMemory("I'm redesigning the Lore shell.", Meta(MemoryStatuses.Staged));

        await using LoreApiHarness harness = await StartAsync();
        JsonElement body = await harness.Client.GetFromJsonAsync<JsonElement>($"/memories/{id}/evidence");
        JsonElement ep = Assert.Single(body.GetProperty("episodes").EnumerateArray().ToArray());

        Assert.Equal("ep-1", ep.GetProperty("id").GetString());
        Assert.Equal(12, ep.GetProperty("observation_count").GetInt32());
        Assert.Equal(
            "Figma.exe",
            Assert.Single(ep.GetProperty("executables").EnumerateArray().ToArray()).GetString());
        Assert.Equal(
            "Figma — Lore rebrand",
            Assert.Single(ep.GetProperty("titles").EnumerateArray().ToArray()).GetString());
        // Samples are the evidence: a window title alone does not answer "why does Lore
        // think this?". Same data and same filter chain as /episodes.
        Assert.True(ep.TryGetProperty("samples", out JsonElement samples));
        Assert.NotEmpty(samples.EnumerateArray().ToArray());
    }

    [Fact]
    public async Task Evidence_404s_for_an_unknown_memory()
    {
        await using LoreApiHarness harness = await StartAsync();

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await harness.Client.GetAsync(U("/memories/nope/evidence"))).StatusCode);
    }

    [Fact]
    public async Task Evidence_is_empty_when_no_episode_row_backs_the_ids()
    {
        // The memory references ep-1 but no episode was persisted (e.g. swept) — an empty list,
        // not a 404 or a 500.
        string id = SeedMemory("Orphaned evidence.", Meta(MemoryStatuses.Active));

        await using LoreApiHarness harness = await StartAsync();
        JsonElement body = await harness.Client.GetFromJsonAsync<JsonElement>($"/memories/{id}/evidence");

        Assert.Empty(body.GetProperty("episodes").EnumerateArray().ToArray());
    }

    [Fact]
    public async Task Evidence_returns_the_episodes_that_survived_retention()
    {
        // v2-008 R4.3: retention prunes episodes and never memories, so a memory routinely
        // outlives some of its evidence. The answer is the survivors — not an error, and not a
        // hole in the list for the id that no longer resolves.
        await _activity.SaveEpisodeAsync(new Episode(
            "ep-2", _time.Now.AddMinutes(-30), _time.Now.AddMinutes(-10),
            ["Figma.exe"], ["Figma — Lore rebrand"], ["sample text"], 12, []));
        string id = SeedMemory(
            "I'm redesigning the Lore shell.",
            new MemoryMetadata(
                MemoryKinds.State, MemoryStatuses.Active, 0.8,
                _time.Now.ToUnixTimeSeconds() + 14 * 86_400, 0,
                MemoryUpdateReasons.Staged, Episodes: ["ep-1", "ep-2"])); // ep-1 was pruned

        await using LoreApiHarness harness = await StartAsync();
        JsonElement body = await harness.Client.GetFromJsonAsync<JsonElement>($"/memories/{id}/evidence");

        JsonElement ep = Assert.Single(body.GetProperty("episodes").EnumerateArray().ToArray());
        Assert.Equal("ep-2", ep.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Economy_counts_todays_decisions_against_the_budget()
    {
        await _activity.LogDecisionAsync(new DecisionEntry(
            _time.Now.AddHours(-2), "ep-1", "promoted", "x", "s", "state", "m1"));
        await _activity.LogDecisionAsync(new DecisionEntry(
            _time.Now.AddHours(-1), "ep-2", "no_facts", "x", "", "", ""));
        await _activity.LogDecisionAsync(new DecisionEntry(
            _time.Now.AddDays(-2), "ep-0", "promoted", "yesterday", "s", "state", "m0"));

        await using LoreApiHarness harness = await StartAsync();
        JsonElement economy = await harness.Client.GetFromJsonAsync<JsonElement>("/system/economy");

        Assert.Equal(1, economy.GetProperty("promoted_today").GetInt64());
        Assert.Equal(
            ConfiguredBudget, economy.GetProperty("daily_budget").GetInt64());
        Assert.Equal(
            1, economy.GetProperty("decisions_today").GetProperty("no_facts").GetInt64());
    }

    [Fact]
    public async Task Staging_promote_activates_with_user_authority()
    {
        string id = SeedMemory("I'm looking for a new job.", Meta(MemoryStatuses.Staged));

        await using LoreApiHarness harness = await StartAsync();
        HttpResponseMessage response = await harness.Client.PostAsync(U($"/staging/{id}/promote"), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement metadata = body.GetProperty("metadata");
        Assert.Equal("active", metadata.GetProperty("status").GetString());
        Assert.True(metadata.GetProperty("user_edited").GetBoolean());
        Assert.True(metadata.GetProperty("established_at").GetInt64() > 0);

        DecisionEntry decision = Assert.Single(await _activity.GetRecentDecisionsAsync());
        Assert.Equal("user_promoted", decision.Action);
        Assert.Equal(id, decision.MemoryId);
    }

    [Fact]
    public async Task Staging_dismiss_archives()
    {
        string id = SeedMemory("Noise candidate.", Meta(MemoryStatuses.Staged));

        await using LoreApiHarness harness = await StartAsync();
        HttpResponseMessage response = await harness.Client.PostAsync(U($"/staging/{id}/dismiss"), null);

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("archived", body.GetProperty("metadata").GetProperty("status").GetString());
        Assert.Equal("user_dismissed", Assert.Single(await _activity.GetRecentDecisionsAsync()).Action);
    }

    [Fact]
    public async Task Staging_actions_reject_non_staged_memories()
    {
        string id = SeedMemory("Already active.", Meta(MemoryStatuses.Active));

        await using LoreApiHarness harness = await StartAsync();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await harness.Client.PostAsync(U($"/staging/{id}/promote"), null)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await harness.Client.PostAsync(U("/staging/nope/promote"), null)).StatusCode);
    }

    [Fact]
    public async Task Confirm_extends_a_state_horizon_and_restamps()
    {
        var meta = new MemoryMetadata(
            MemoryKinds.State, MemoryStatuses.Active, 0.6,
            _time.Now.ToUnixTimeSeconds() + 2 * 86_400, // about to expire
            _time.Now.ToUnixTimeSeconds() - 20 * 86_400,
            MemoryUpdateReasons.Promoted);
        string id = SeedMemory("I'm recovering from a wisdom tooth extraction.", meta);

        await using LoreApiHarness harness = await StartAsync();
        HttpResponseMessage response = await harness.Client.PostAsync(U($"/memories/{id}/confirm"), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement metadata = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("metadata");
        long expiresAt = metadata.GetProperty("expires_at").GetInt64();
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 45 * 86_400, expiresAt); // default horizon out
        Assert.Equal("confirmed", metadata.GetProperty("updated_reason").GetString());
        Assert.Equal(0.9, metadata.GetProperty("confidence").GetDouble());
    }

    [Fact]
    public async Task Library_list_filters_by_kind_and_status()
    {
        SeedMemory("I'm recovering from surgery.", Meta(MemoryStatuses.Active, MemoryKinds.State));
        SeedMemory("I visited France.", Meta(MemoryStatuses.Active, MemoryKinds.Experience));
        SeedMemory("Staged candidate.", Meta(MemoryStatuses.Staged, MemoryKinds.State));

        await using LoreApiHarness harness = await StartAsync();

        JsonElement states = await harness.Client.GetFromJsonAsync<JsonElement>(
            "/memories?kind=state&status=active");
        JsonElement only = Assert.Single(states.GetProperty("items").EnumerateArray().ToArray());
        Assert.Contains("surgery", only.GetProperty("memory").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, states.GetProperty("total").GetInt64());

        JsonElement staged = await harness.Client.GetFromJsonAsync<JsonElement>(
            "/memories?status=staged");
        Assert.Single(staged.GetProperty("items").EnumerateArray().ToArray());

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await harness.Client.GetAsync(U("/memories?kind=vibe"))).StatusCode);
    }

    [Fact]
    public async Task Patch_carries_user_authority()
    {
        string id = SeedMemory("I live in Boston.", Meta(MemoryStatuses.Active, MemoryKinds.Identity));

        await using LoreApiHarness harness = await StartAsync();
        using JsonContent patchBody = JsonContent.Create(new { text = "I live in Cambridge.", pinned = true });
        HttpResponseMessage response = await harness.Client.PatchAsync(U($"/memories/{id}"), patchBody);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("I live in Cambridge.", body.GetProperty("memory").GetString());
        JsonElement metadata = body.GetProperty("metadata");
        Assert.True(metadata.GetProperty("user_edited").GetBoolean());
        Assert.True(metadata.GetProperty("pinned").GetBoolean());
        Assert.Equal(1.0, metadata.GetProperty("confidence").GetDouble());
        Assert.Equal("user_edit", metadata.GetProperty("updated_reason").GetString());

        // A patch with nothing to change is a 400, not a silent no-op.
        using JsonContent emptyBody = JsonContent.Create(new { });
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await harness.Client.PatchAsync(U($"/memories/{id}"), emptyBody)).StatusCode);
    }
}
