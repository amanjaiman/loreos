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

        // This assertion has moved twice, and the history is the point. Originally it read
        // DoesNotContain("tea", "weak-state"): both are on-topic enough for a food question
        // (0.48 and 0.55 semantic) but the blend multiplied them down by their confidence
        // (0.8 and 0.3) below the floor. v2-008 R5.3 moved the floor onto the semantic
        // score, and both came back — correct for `tea`, which is a real 0.8-confidence
        // preference, and wrong for `weak-state`, a 0.3-confidence "I might be coming down
        // with a cold" that no caller should spend context on.
        //
        // v2-008 R5.4 adds MinConfidence (0.5) as a SECOND inclusion gate, so `weak-state`
        // drops out again while `tea` stays. Note that it is dropped despite scoring HIGHER
        // semantically than the row that survives (0.55 vs 0.48): the two gates measure
        // genuinely different things, which is why neither can be expressed as the other.
        Assert.Equal(["wisdom", "tea"], hits.Select(hit => hit.Id).ToArray());

        // What must NOT change: the dental state still wins outright. The floor's "no
        // padding" guarantee is proven separately by Unrelated_query_returns_empty_not_padded
        // below — 0.30/0.22 pairs still return nothing at all.
        Assert.True(top.Score > 0.5);
    }

    [Fact]
    public async Task Wisdom_teeth_test_expired_state_never_surfaces()
    {
        _time.Now = GoldenCorpus.Now.AddDays(45); // past the 30-day horizon

        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("should I order takeout tonight");

        Assert.DoesNotContain(hits, hit => hit.Id == "wisdom");
    }

    // ── v2-008 R5.4: confidence is an inclusion gate again, but its own gate ─────────────
    [Fact]
    public async Task Confidence_gates_inclusion_independently_of_relevance()
    {
        // The corpus row that makes this observable is `weak-state`: 0.55 semantic on the
        // takeout query (comfortably over the 0.47 floor) at 0.3 confidence. It is excluded
        // by MinConfidence alone — relevance never rejected it.
        RecallService service = Build();
        Assert.DoesNotContain(
            await service.RecallAsync("should I order takeout tonight"),
            hit => hit.Id == "weak-state");

        // …and it is the CONFIDENCE gate doing it, not some other change: drop
        // MinConfidence below 0.3 and the same row returns on the same query. This is what
        // makes the two gates separable rather than one threshold wearing two hats.
        var permissive = new RecallService(
            new GoldenMemoryService(), new RecallOptions { MinConfidence = 0.25 }, _time);
        Assert.Contains(
            await permissive.RecallAsync("should I order takeout tonight"),
            hit => hit.Id == "weak-state");

        // The gate is >=, not >: a memory sitting exactly on the threshold is kept, so the
        // default 0.5 excludes "below half-confident" rather than "half-confident".
        var exact = new RecallService(
            new GoldenMemoryService(), new RecallOptions { MinConfidence = 0.3 }, _time);
        Assert.Contains(
            await exact.RecallAsync("should I order takeout tonight"),
            hit => hit.Id == "weak-state");
    }

    [Fact]
    public async Task Real_confidence_levels_are_all_comfortably_above_the_gate()
    {
        // MinConfidence's calibration claim, pinned: 0.5 is set to exclude speculation, not
        // to trim ordinary memories. Every non-hunch row in the corpus — the wisdom-teeth
        // relevance case included — sits at 0.8+, and real captured bookings at 1.0, so the
        // gate has margin and the R5.3 age fix is untouched by it. If a future capture
        // change starts emitting 0.4-confidence facts routinely, this test is where that
        // shows up as a calibration question rather than as silently missing recall.
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync("tell me about myself", k: 10);
        Assert.Equal(
            ["wisdom", "boston", "loreos", "tea", "france"],
            hits.Select(hit => hit.Id).ToArray());

        IReadOnlyList<RecallHit> flights = await Build().RecallAsync(
            "book a flight to Denver", k: 30, kinds: [MemoryKinds.Experience]);
        Assert.Equal(8, flights.Count);
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
        //
        // v2-008 R5.4 made that cost steeper without changing the outcome: at seven years
        // the marathon is far past saturation, so lowering ExperienceDecayFloor to 0.2 cut
        // its blended score from ~0.29 to ~0.10. It still returns — the semantic gate, not
        // the decay floor, is what guarantees that since R5.3 — and it still sits below
        // France. This is the one place outside the flight corpus where the new decay floor
        // is visible, and it moves a score, not an order.
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
    // the SEMANTIC score against Floor, ordering still by the blend. These tests replaced
    // T001's regression guard, which deliberately pinned the broken behaviour.
    //
    // R5.3 delivered the returning half of its acceptance and not the ordering half: with
    // ExperienceDecayFloor at 0.6 the temporal factor saturated at ~187 days, so the five
    // oldest rows all blended to the same 0.44-0.45 and sorted by embedder noise across a
    // 0.8140-0.8309 band (16mo above 8mo; 8mo and 24mo tying exactly). T010 asserted that
    // as a known gap. v2-008 R5.4 closes it by lowering ExperienceDecayFloor to 0.2 —
    // which is safe precisely BECAUSE of R5.3: the decay floor can no longer exclude
    // anything, so it is free to keep ordering out to ~587 days (365 x -ln(0.2)) instead
    // of ~187. Floor, ExperienceWeight, ExperienceDecayDays and Blend remain untouched.
    [Fact]
    public async Task Flight_booking_history_returns_every_booking_however_old()
    {
        IReadOnlyList<RecallHit> hits = await Build().RecallAsync(
            "book a flight to Denver", k: 30, kinds: [MemoryKinds.Experience]);

        // All eight, none dropped by the floor, in STRICT RECENCY ORDER — the full R5.2 /
        // R5.3 / R5.4 acceptance criterion. Each returns on its own ~0.82 relevance to the
        // query; none has to earn its place a second time by being recent enough, and age
        // now decides only where in the list it lands. This assertion inverts T010's, which
        // pinned the noisy order 1mo, 3mo, 5mo, 16mo, 20mo, 12mo, 8mo, 24mo.
        Assert.Equal(
            [
                "flight-1mo",
                "flight-3mo",
                "flight-5mo",
                "flight-8mo",
                "flight-12mo",
                "flight-16mo",
                "flight-20mo",
                "flight-24mo",
            ],
            hits.Select(hit => hit.Id).ToArray());

        // Pinned scores, computed from the measured similarities in GoldenCorpus through
        // semantic x 0.9 x max(0.2, exp(-days/365)) x 1.0. Note how far apart they now
        // spread — 0.68 down to 0.15, against the 0.68/0.58/0.49 + a flat 0.44 clump the
        // 0.6 floor produced. That spread IS the recency signal being audible again.
        Dictionary<string, double> scoresById = hits.ToDictionary(hit => hit.Id, hit => hit.Score);
        Assert.Equal(0.6788, scoresById["flight-1mo"], precision: 3);
        Assert.Equal(0.5786, scoresById["flight-3mo"], precision: 3);
        Assert.Equal(0.4858, scoresById["flight-5mo"], precision: 3);
        Assert.Equal(0.3796, scoresById["flight-8mo"], precision: 3);
        Assert.Equal(0.2705, scoresById["flight-12mo"], precision: 3);
        Assert.Equal(0.2007, scoresById["flight-16mo"], precision: 3);
        Assert.Equal(0.1485, scoresById["flight-20mo"], precision: 3);
        Assert.Equal(0.1465, scoresById["flight-24mo"], precision: 3);

        // The gap R5.4 narrowed but did not abolish, recorded honestly. Saturation moved to
        // ~587 days, so the 20- and 24-month rows (600 and 730 days) are STILL both pinned
        // at ExperienceDecayFloor and are still separated by embedder noise (0.8250 vs
        // 0.8140) rather than by age. Their order happens to agree with recency here; that
        // is a coincidence of this corpus, not a property of the scoring. Everything up to
        // 16 months is genuinely recency-ordered, which is the real improvement. Pushing
        // saturation past a two-year history would mean lowering the floor further or
        // raising ExperienceDecayDays — a further calibration, not a bug.
        Assert.Equal(
            scoresById["flight-20mo"] / 0.8250,
            scoresById["flight-24mo"] / 0.8140,
            precision: 3);
        Assert.True(
            scoresById["flight-16mo"] > scoresById["flight-20mo"],
            "16 months is inside the live-decay window and must outrank 20 months on age");
    }

    [Fact]
    public async Task Oldest_booking_clears_the_floor_on_relevance_and_ranks_last_on_the_blend()
    {
        // The two-year-old booking is the case R5.3 exists for, so pin both halves of its
        // new treatment. Its RELEVANCE — the only question the floor now asks — clears by
        // a wide margin (~0.34), because a booking is a booking however long ago it was
        // made; the embedder never thought otherwise. Its STRENGTH still reflects its age:
        // the blend puts it far BELOW the floor (~0.15 since R5.4 lowered the decay floor
        // to 0.2; ~0.44 before), which is exactly the region where the old code dropped it.
        // Same number, different job. If those two ever converge again, inclusion has
        // silently gone back to being an age test.
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
        // The decay floor — 0.2 since v2-008 R5.4, previously 0.6. It bites at ~587 days
        // rather than ~187, and it can no longer exclude anything: inclusion has been the
        // semantic floor's job since R5.3, so this only ever scales rank now.
        Assert.Equal(0.8 * 0.9 * 0.2, ancient, precision: 10);
        Assert.Equal(
            587.4, 365.0 * -Math.Log(Options.ExperienceDecayFloor), precision: 1);
    }
}
