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

    // ── v2-008 T001: the recall-floor verification gate (spec R5.2) ──────────────────
    //
    // R1.3's design is: Lore stores eight flight bookings as `experience` rows and the
    // *consuming agent* infers the pattern ("mostly window seats, though the last was
    // aisle") from the whole set at recall time. That only works if `recall(k: 30,
    // kinds: [experience])` actually returns all eight. This test answers that question
    // against REAL nomic-embed-text similarities (see GoldenCorpus's "book a flight to
    // Denver" entry — measured live, not guessed) run through the real blend/floor math
    // in RecallScorer/RecallService.
    //
    // ANSWER: NO. Only the 3 most recent bookings clear the floor; the other 5 do not.
    //
    // Why: nomic-embed-text scores every flight statement against the query in a tight
    // 0.81-0.83 band regardless of which flight it is — it separates "a flight I booked"
    // from unrelated topics, not one flight from another. So the semantic term is nearly
    // constant (~0.82) across all eight, and what actually decides pass/fail is
    // ExperienceWeight (0.9) x the temporal factor. Once an experience is old enough that
    // temporal decay has saturated at ExperienceDecayFloor (0.6) — which happens at
    // ~187 days (~6.1 months), per exp(-x/365) = 0.6 — every older row gets the SAME
    // ceiling: 0.9 x 0.6 = 0.54. At the measured ~0.82 semantic score that ceiling lands
    // around 0.44-0.45, which sits BELOW Floor (0.47) by ~0.03. So every flight past the
    // ~6-month mark is dropped, not just the oldest ones, and raising k cannot recover
    // them — they never clear the floor to begin with.
    //
    // Binding constraint: the Floor (0.47) vs. the ExperienceWeight x ExperienceDecayFloor
    // ceiling (0.9 x 0.6 = 0.54) leaves only ~0.06 of headroom in the temporal-weight
    // product, and real-world same-topic semantic scores (~0.82, not the ~0.95+ it would
    // take to clear) eat that headroom. This is a design tension between R5.2's floor
    // calibration (T010, tuned to keep unrelated pairs out) and R1.3's premise (surface an
    // aging set), not a bug in either piece alone — see the failing/dropped assertions
    // below for the exact scores. Per T001's brief, this is NOT fixed here by lowering the
    // floor or raising ExperienceWeight/decay — that tradeoff is calibrated against the
    // live embedding distribution and out of this task's scope. This test is the
    // regression guard: it pins today's (incomplete) behavior so any future change to
    // Floor, ExperienceWeight, ExperienceDecayDays, or ExperienceDecayFloor is forced to
    // consciously re-examine this gate rather than silently drift.
    [Fact]
    public async Task T001_flight_booking_history_only_recent_bookings_clear_the_floor()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync(
            "book a flight to Denver", k: 30, kinds: [MemoryKinds.Experience]);

        // What DOES come back: the 3 most recent bookings, recency-weighted (as the spec
        // requires for whatever does clear the floor).
        Assert.Equal(
            ["flight-1mo", "flight-3mo", "flight-5mo"],
            hits.Select(hit => hit.Id).ToArray());
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.True(hits[1].Score > hits[2].Score);

        // What does NOT come back: the 5 bookings 8 months and older. Spec R5.2's
        // acceptance criterion — "a store seeded with eight flight bookings ... returns
        // all eight ... none dropped by the floor" — is NOT met. This assertion documents
        // that gap; it is expected to keep passing until Floor/ExperienceWeight/decay are
        // deliberately revisited.
        Assert.DoesNotContain(
            hits,
            hit => hit.Id is "flight-8mo" or "flight-12mo" or "flight-16mo"
                or "flight-20mo" or "flight-24mo");

        // Pin the actual blended scores (rounded to 4dp by RecallService) for the ones
        // that DO return, so a silent change to the weights/decay/floor is caught here
        // rather than discovered later against a live corpus.
        Dictionary<string, double> scoresById = hits.ToDictionary(hit => hit.Id, hit => hit.Score);
        Assert.Equal(0.6788, scoresById["flight-1mo"], precision: 3);
        Assert.Equal(0.5786, scoresById["flight-3mo"], precision: 3);
        Assert.Equal(0.4858, scoresById["flight-5mo"], precision: 3);
    }

    [Fact]
    public void T001_dropped_flights_score_just_under_the_floor_not_far_under_it()
    {
        // Recompute the blend directly (bypassing the floor filter) for the oldest
        // booking to show HOW CLOSE it comes: this is not a case of stale bookings being
        // wildly irrelevant, it's ~0.03 short of the 0.47 floor, entirely because
        // ExperienceWeight x ExperienceDecayFloor caps out at 0.54 once decay saturates.
        MemoryMetadata oldest = new(
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0,
            MemoryMetadata.FarFutureUnixSeconds,
            GoldenCorpus.Now.ToUnixTimeSeconds() - 730 * 86_400L,
            MemoryUpdateReasons.Promoted);

        double blended = RecallScorer.Blend(
            0.8140, oldest, GoldenCorpus.Now.ToUnixTimeSeconds(), _options);

        Assert.True(blended < _options.Floor, $"expected below floor, got {blended}");
        Assert.True(
            blended > _options.Floor - 0.05,
            $"expected a near miss (within 0.05 of the floor), got {blended}");
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
