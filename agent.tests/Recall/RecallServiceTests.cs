using Lore.Agent.Memory;
using Lore.Agent.Recall;

namespace Lore.Agent.Tests.Recall;

/// <summary>Golden-corpus contract tests for the recall blend/floor/filters —
/// spec v2-001 AC 1, 2, and 6 in deterministic form.</summary>
public sealed class RecallServiceTests
{
    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = GoldenCorpus.Now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly FixedTime _time = new();
    private readonly RecallOptions _options = new();

    private RecallService Build() => new(new GoldenMemoryService(), _options, _time);

    [Fact]
    public async Task Wisdom_teeth_test_food_question_surfaces_the_dental_state()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("should I order takeout tonight");

        RecallHit top = hits[0];
        Assert.Equal("wisdom", top.Id);
        Assert.Equal(MemoryKinds.State, top.Kind);
        // The tea preference (0.48 raw) and the weak cold hunch (0.55 raw × 0.3
        // confidence) stay under the floor — no padding with plausible noise.
        Assert.DoesNotContain(hits, hit => hit.Id is "tea" or "weak-state");
    }

    [Fact]
    public async Task Wisdom_teeth_test_expired_state_never_surfaces()
    {
        _time.Now = GoldenCorpus.Now.AddDays(45); // past the 30-day horizon

        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("should I order takeout tonight");

        Assert.DoesNotContain(hits, hit => hit.Id == "wisdom");
    }

    [Fact]
    public async Task France_test_travel_question_surfaces_the_trip_and_only_live_rows()
    {
        IReadOnlyList<RecallHit> hits = await Build()
            .RecallAsync("what travel destination should I pick next");

        Assert.Contains(hits, hit => hit.Id == "france");
        // Staged and archived rows carry HIGHER raw similarity in the corpus; only
        // the status/expiry filters keep them out.
        Assert.DoesNotContain(hits, hit => hit.Id is "staged-jobs" or "archived-city");
    }

    [Fact]
    public async Task Unrelated_query_returns_empty_not_padded()
    {
        Assert.Empty(await Build().RecallAsync("how do I cook pasta carbonara"));
    }

    [Fact]
    public async Task Kind_restriction_limits_results()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync(
            "tell me about myself", kinds: [MemoryKinds.Preference]);

        Assert.Equal(["tea"], hits.Select(hit => hit.Id).ToArray());
    }

    [Fact]
    public async Task K_caps_the_result_count_strongest_first()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("tell me about myself", k: 2);

        Assert.Equal(2, hits.Count);
        Assert.True(hits[0].Score >= hits[1].Score);
    }

    [Fact]
    public async Task Old_experiences_decay_below_current_facts_at_equal_similarity()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("tell me about myself", k: 10);

        int boston = hits.ToList().FindIndex(hit => hit.Id == "boston");
        int france = hits.ToList().FindIndex(hit => hit.Id == "france");
        Assert.True(boston >= 0 && france >= 0);
        // france has higher raw similarity (0.85 vs 0.80) but decays as an experience.
        Assert.True(boston < france, "identity should outrank the decayed experience");
    }

    [Fact]
    public async Task Query_is_required()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Build().RecallAsync("   "));
    }
}

public sealed class RecallScorerTests
{
    private static readonly RecallOptions Options = new();
    private static readonly long Now = GoldenCorpus.Now.ToUnixTimeSeconds();

    private static MemoryMetadata Meta(
        string kind, double confidence = 1.0, int establishedDaysAgo = 0) => new(
        kind, MemoryStatuses.Active, confidence,
        MemoryMetadata.FarFutureUnixSeconds, Now - establishedDaysAgo * 86_400L,
        MemoryUpdateReasons.Promoted);

    [Fact]
    public void Rows_without_v2_metadata_score_zero()
    {
        Assert.Equal(0.0, RecallScorer.Blend(0.99, null, Now, Options));
    }

    [Fact]
    public void State_gets_its_boost_and_confidence_damps()
    {
        double state = RecallScorer.Blend(0.6, Meta(MemoryKinds.State), Now, Options);
        double identity = RecallScorer.Blend(0.6, Meta(MemoryKinds.Identity), Now, Options);
        double weak = RecallScorer.Blend(0.6, Meta(MemoryKinds.State, confidence: 0.3), Now, Options);

        Assert.Equal(0.6 * 1.15, state, precision: 10);
        Assert.Equal(0.6, identity, precision: 10);
        Assert.Equal(state * 0.3, weak, precision: 10);
    }

    [Fact]
    public void Experience_decay_is_gentle_and_floored()
    {
        double fresh = RecallScorer.Blend(0.8, Meta(MemoryKinds.Experience), Now, Options);
        double aging = RecallScorer.Blend(
            0.8, Meta(MemoryKinds.Experience, establishedDaysAgo: 90), Now, Options);
        double ancient = RecallScorer.Blend(
            0.8, Meta(MemoryKinds.Experience, establishedDaysAgo: 3650), Now, Options);

        Assert.Equal(0.8 * 0.9, fresh, precision: 10);
        Assert.Equal(fresh * Math.Exp(-90.0 / 365.0), aging, precision: 10);
        Assert.Equal(0.8 * 0.9 * 0.6, ancient, precision: 10); // the decay floor
    }
}
