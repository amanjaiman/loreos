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

        // CHANGED BY v2-008 R5.3, deliberately. This assertion used to read
        // DoesNotContain("tea", "weak-state") — both are on-topic enough for a food
        // question (0.48 and 0.55 semantic) and were excluded only because the blend
        // multiplied them down: tea by its 0.8 confidence, the cold hunch by its 0.3.
        // Now that the floor asks a purely semantic question, confidence no longer gates
        // INCLUSION for any kind; it gates rank. So a low-confidence hunch that is
        // genuinely about the topic comes back, carrying a score that says how little to
        // trust it. That is the intended trade — an omission is invisible to a caller,
        // whereas a 0.19 score is legible — but it is a real behaviour change beyond the
        // aged-experience case R5.3 set out to fix, so it is asserted here rather than
        // left to be discovered.
        Assert.Equal(["wisdom", "tea", "weak-state"], hits.Select(hit => hit.Id).ToArray());

        // What must NOT change: the dental state still wins outright, and the weak hunch
        // is ranked last by a wide margin, not smuggled in as a peer. The floor's real
        // "no padding" guarantee is unweakened and is proven by
        // Unrelated_query_returns_empty_not_padded below — 0.30/0.22 pairs still return
        // nothing at all.
        Assert.True(top.Score > 0.5);
        Assert.True(hits[^1].Id == "weak-state" && hits[^1].Score < 0.2);
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
        // the status/expiry filters keep them out. Those filters run at query time and
        // are untouched by R5.3 — moving the floor onto the semantic score does not give
        // a high-similarity dead row a way back in.
        Assert.DoesNotContain(hits, hit => hit.Id is "staged-jobs" or "archived-city");

        // Added by v2-008 R5.3: the 2019 marathon (0.60 semantic, seven years old) now
        // returns where it used to be dropped at 0.29 blended. This is the same fix as the
        // aged-booking case, on a memory that has nothing to do with flights — evidence
        // that R5.3 restores the documented "experiences fade gently with age but never
        // vanish" invariant generally, not just for the corpus the spec was written
        // around. It still ranks below France, which is what its age should cost it.
        int france = hits.ToList().FindIndex(hit => hit.Id == "france");
        int marathon = hits.ToList().FindIndex(hit => hit.Id == "marathon-old");
        Assert.True(marathon >= 0, "an aged but relevant experience must not vanish");
        Assert.True(france < marathon, "recency should still decide rank");
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

    [Fact]
    public async Task Rows_without_v2_metadata_are_excluded_even_at_high_similarity()
    {
        // Before v2-008 R5.3 these were excluded as a side effect: Blend returns 0.0 for a
        // row with no v2 metadata, and 0.0 never cleared the floor. The semantic score
        // carries no such signal — an unmigrated row can be a near-perfect match — so
        // RecallService now excludes them by an explicit null check. Without it a 0.99
        // similarity would sail past the floor and NRE on the metadata deref. This test
        // is that check's only guard.
        var service = new RecallService(new UnmigratedMemoryService(), _options, _time);

        Assert.Empty(await service.RecallAsync("tell me about myself"));
    }

    /// <summary>A store holding one pre-v2 row: high similarity, no metadata at all.</summary>
    private sealed class UnmigratedMemoryService : GoldenMemoryService
    {
        public override Task<IReadOnlyList<MemoryRecord>> SearchAsync(
            string query, string userId = "default", int limit = 10,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>(
                [new MemoryRecord("legacy", "I live in Boston.", 0.99, null)]);
    }

    // ── v2-008 R5.3: the floor applies to relevance, not to the blend ────────────────
    //
    // R1.3's design is: Lore stores eight flight bookings as `experience` rows and the
    // *consuming agent* infers the pattern ("mostly window seats, though the last was
    // aisle") from the whole set at recall time. That only works if `recall(k: 30,
    // kinds: [experience])` actually returns all eight. These tests answer that question
    // against REAL nomic-embed-text similarities (see GoldenCorpus's "book a flight to
    // Denver" entry — measured live, not guessed) run through the real blend/floor math
    // in RecallScorer/RecallService.
    //
    // T001 ran this gate and it FAILED: only the 3 most recent bookings came back. The
    // cause was that Floor was compared against the BLENDED score. nomic-embed-text scores
    // every flight statement in a tight 0.81-0.83 band regardless of which flight it is —
    // it separates "a flight I booked" from unrelated topics, not one flight from another
    // — so the semantic term was near-constant and what decided pass/fail was
    // ExperienceWeight (0.9) x the temporal factor. Past ~187 days (exp(-x/365) = 0.6)
    // decay saturates at ExperienceDecayFloor, giving every older row the same ceiling of
    // 0.9 x 0.6 = 0.54; at ~0.82 semantic that lands at 0.44-0.45, just under the 0.47
    // floor. Every experience older than ~6 months was unrecallable at any similarity —
    // the opposite of the invariant RecallScorer documents.
    //
    // R5.3's fix separates the two jobs the floor was doing: inclusion is now decided by
    // the SEMANTIC score against Floor, ordering still by the blend. Floor,
    // ExperienceWeight, ExperienceDecayDays, ExperienceDecayFloor and Blend are all
    // unchanged. These tests replace T001's regression guard, which deliberately pinned
    // the broken behaviour so this task would have to invert it.
    [Fact]
    public async Task Flight_booking_history_returns_every_booking_however_old()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync(
            "book a flight to Denver", k: 30, kinds: [MemoryKinds.Experience]);

        // All eight, none dropped by the floor — spec R5.2/R5.3's acceptance criterion.
        // Each returns on its own ~0.82 relevance to the query; none of them has to earn
        // its place a second time by being recent or certain enough.
        Assert.Equal(
            [
                "flight-1mo", "flight-3mo", "flight-5mo", "flight-16mo",
                "flight-20mo", "flight-12mo", "flight-8mo", "flight-24mo",
            ],
            hits.Select(hit => hit.Id).ToArray());

        // Ranking is still the blend's job, and within the window where recency decay is
        // still live (< ~187 days) it orders these newest-first, as R5.3 intends.
        Dictionary<string, double> scoresById = hits.ToDictionary(hit => hit.Id, hit => hit.Score);
        Assert.Equal(0.6788, scoresById["flight-1mo"], precision: 3);
        Assert.Equal(0.5786, scoresById["flight-3mo"], precision: 3);
        Assert.Equal(0.4858, scoresById["flight-5mo"], precision: 3);
        Assert.True(scoresById["flight-1mo"] > scoresById["flight-3mo"]);
        Assert.True(scoresById["flight-3mo"] > scoresById["flight-5mo"]);
        Assert.True(scoresById["flight-5mo"] > scoresById["flight-8mo"]);

        // KNOWN GAP, asserted rather than glossed: past ~187 days the temporal factor is
        // pinned at ExperienceDecayFloor (0.6) for everything, so the blend degenerates to
        // semantic x 0.9 x confidence and age stops contributing at all. Among the five
        // saturated rows the order is therefore set by embedder noise across a 0.8140-
        // 0.8309 band, not by recency: the 16-month booking outranks the 8-month one, and
        // the 8- and 24-month rows tie exactly (identical 0.8140 similarity). R5.3's
        // acceptance says these should come back "ordered recent-first"; the returning
        // half is delivered here, the ordering half is not, and it cannot be without
        // changing ExperienceDecayFloor/ExperienceDecayDays — which T010 is scoped out of.
        // This assertion exists so that gap is a recorded fact, not a silent surprise.
        Assert.True(
            scoresById["flight-16mo"] > scoresById["flight-8mo"],
            "saturated-decay ordering is semantic noise, not recency — see comment above");
        foreach (string saturated in new[]
                 { "flight-8mo", "flight-12mo", "flight-16mo", "flight-20mo", "flight-24mo" })
        {
            Assert.InRange(scoresById[saturated], 0.4396, 0.4487);
        }
    }

    [Fact]
    public async Task Oldest_booking_clears_the_floor_on_relevance_and_ranks_last_on_the_blend()
    {
        // The two-year-old booking is the case R5.3 exists for, so pin both halves of its
        // new treatment. Its RELEVANCE — the only question the floor now asks — clears by
        // a wide margin (~0.34), because a booking is a booking however long ago it was
        // made; the embedder never thought otherwise. Its STRENGTH still reflects its age:
        // the blend puts it ~0.03 BELOW the floor, which is exactly where the old code
        // dropped it. Same number, different job. If those two ever converge again,
        // inclusion has silently gone back to being an age test.
        const double OldestSemantic = 0.8140; // measured, see GoldenCorpus
        MemoryMetadata oldest = new(
            MemoryKinds.Experience, MemoryStatuses.Active, 1.0,
            MemoryMetadata.FarFutureUnixSeconds,
            GoldenCorpus.Now.ToUnixTimeSeconds() - 730 * 86_400L,
            MemoryUpdateReasons.Promoted);

        double blended = RecallScorer.Blend(
            OldestSemantic, oldest, GoldenCorpus.Now.ToUnixTimeSeconds(), _options);

        Assert.True(
            OldestSemantic - _options.Floor > 0.3,
            $"expected relevance to clear the floor comfortably, got {OldestSemantic}");
        Assert.True(
            blended < _options.Floor,
            $"expected the blend to still rank it below the floor's value, got {blended}");

        // And end-to-end: it comes back regardless.
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("book a flight to Denver", k: 30);
        Assert.Contains("flight-24mo", hits.Select(hit => hit.Id));
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
