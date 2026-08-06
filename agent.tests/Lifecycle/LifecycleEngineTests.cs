using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Inference;
using Lore.Agent.Lifecycle;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Lifecycle;

public sealed class LifecycleEngineTests : IDisposable
{
    /// <summary>A fixed non-UTC zone. Without this the fake would inherit the machine's, so
    /// these tests would pass on a UTC CI runner whatever the day boundary did — the whole
    /// point is that "today" follows the user's clock, not the server's.</summary>
    private static readonly TimeZoneInfo TestZone =
        TimeZoneInfo.CreateCustomTimeZone("lore-test", TimeSpan.FromHours(-5), "Test", "Test");

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;

        public override TimeZoneInfo LocalTimeZone => TestZone;
    }

    /// <summary>Routes by prompt: distillation requests get <see cref="DistillJson"/>;
    /// arbitration requests get <see cref="ArbitrationAnswer"/>.</summary>
    private sealed class ScriptedBackend : IInferenceBackend
    {
        public string DistillJson { get; set; } = """{"facts": []}""";

        public string? ArbitrationAnswer { get; set; } = "COEXIST";

        public bool ThrowOnArbitration { get; set; }

        public int ArbitrationCalls { get; private set; }

        public Task<string?> CompleteAsync(
            InferenceRequest request, CancellationToken cancellationToken = default)
        {
            if (request.SystemPrompt.Contains("memory distiller", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>(DistillJson);
            }

            ArbitrationCalls++;
            return ThrowOnArbitration
                ? throw new InvalidOperationException("provider down")
                : Task.FromResult(ArbitrationAnswer);
        }
    }

    /// <summary>Seam fake with scripted search results and recorded writes.</summary>
    private sealed class FakeMemory : IMemoryService
    {
        private int _sequence;

        public List<MemoryRecord> SearchResults { get; } = [];

        public List<(string Statement, MemoryMetadata Metadata)> Stored { get; } = [];

        public List<(string Id, IReadOnlyDictionary<string, object?> Patch)> Patches { get; } = [];

        public List<(string Id, string? Text, IReadOnlyDictionary<string, object?> Patch)> Updates { get; } = [];

        public IReadOnlyDictionary<string, object?>? LastSearchFilters { get; private set; }

        public Task<IReadOnlyList<AddedMemory>> RememberAsync(
            string observation, string userId = "default",
            IReadOnlyDictionary<string, object?>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            MemoryMetadata parsed = MemoryMetadata.From(
                metadata!.ToDictionary(
                    p => p.Key,
                    p => System.Text.Json.JsonSerializer.SerializeToElement(p.Value)))!;
            Stored.Add((observation, parsed));
            IReadOnlyList<AddedMemory> result = [new AddedMemory($"m{++_sequence}", observation, "ADD")];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
            string query, string userId = "default", int limit = 10,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default)
        {
            LastSearchFilters = filters;
            return Task.FromResult<IReadOnlyList<MemoryRecord>>([.. SearchResults]);
        }

        public Task<IReadOnlyList<MemoryRecord>> ListAsync(
            string userId = "default", int limit = 100, int offset = 0,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
            string userId = "default", int count = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
            string userId = "default", CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MemoryRecord?>(null);

        public Task<MemoryRecord?> UpdateAsync(
            string id, string? text = null,
            IReadOnlyDictionary<string, object?>? metadataPatch = null,
            CancellationToken cancellationToken = default)
        {
            Patches.Add((id, metadataPatch!));
            Updates.Add((id, text, metadataPatch!));
            return Task.FromResult<MemoryRecord?>(new MemoryRecord(id, text ?? "patched"));
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static readonly Episode Episode = new(
        "ep-9",
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(20),
        ["browser"],
        ["Wisdom tooth aftercare"],
        ["aftercare text"],
        4);

    private readonly ScriptedBackend _backend = new();
    private readonly FakeMemory _memory = new();
    private readonly ActivityStore _activity = new(":memory:");
    private readonly FakeTimeProvider _time = new();
    // A deliberately tiny budget: these tests are about what happens AT the cap, and
    // seeding the production default's worth of decisions to reach it made them depend on
    // a tunable that exists to be raised (it since was — it is a runaway guard, not a
    // daily allowance). The cap's behaviour is what is under test, not its value.
    private const int TestDailyBudget = 3;

    private readonly LifecycleOptions _options = new() { DailyBudget = TestDailyBudget };

    public void Dispose() => _activity.Dispose();

    private LifecycleEngine BuildEngine() => new(
        new Distiller(_backend, NullLogger<Distiller>.Instance),
        _memory,
        _activity,
        _backend,
        _options,
        _time,
        NullLogger<LifecycleEngine>.Instance);

    private static string FactJson(
        string statement = "I'm recovering from a wisdom tooth extraction.",
        string kind = "state",
        double confidence = 0.7,
        string horizon = "30") =>
        $$"""
        {"facts": [{"statement": "{{statement}}", "kind": "{{kind}}",
                    "confidence": {{confidence}}, "horizon_days": {{horizon}}}]}
        """;

    private MemoryRecord Neighbor(
        string id, string memory, double score, string status, bool pinned = false,
        double confidence = 0.6, string kind = "state")
    {
        var meta = new MemoryMetadata(
            kind, status, confidence,
            _time.Now.ToUnixTimeSeconds() + 100_000, _time.Now.ToUnixTimeSeconds() - 100_000,
            MemoryUpdateReasons.Promoted, Reinforced: 0, Episodes: ["ep-1"], Pinned: pinned);
        return new MemoryRecord(
            id, memory, score,
            meta.ToDictionary().ToDictionary(
                p => p.Key,
                p => System.Text.Json.JsonSerializer.SerializeToElement(p.Value)));
    }

    private async Task<IReadOnlyList<DecisionEntry>> Decisions() =>
        await _activity.GetRecentDecisionsAsync(100);

    // ── the routing table ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Unmatched_ordinary_fact_stages()
    {
        _backend.DistillJson = FactJson();

        await BuildEngine().ProcessAsync(Episode);

        (string statement, MemoryMetadata meta) = Assert.Single(_memory.Stored);
        Assert.Equal(MemoryStatuses.Staged, meta.Status);
        Assert.Equal(
            _time.Now.ToUnixTimeSeconds() + 14 * 86_400, meta.ExpiresAt); // staged TTL
        Assert.Equal(["ep-9"], meta.Episodes);
        Assert.Equal("staged", (await Decisions())[0].Action);
        Assert.Contains("wisdom tooth", statement, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task High_signal_fact_promotes_directly_with_state_horizon()
    {
        _backend.DistillJson = FactJson(confidence: 0.9);

        await BuildEngine().ProcessAsync(Episode);

        (_, MemoryMetadata meta) = Assert.Single(_memory.Stored);
        Assert.Equal(MemoryStatuses.Active, meta.Status);
        Assert.Equal(_time.Now.ToUnixTimeSeconds() + 30 * 86_400, meta.ExpiresAt);
        Assert.Equal("promoted", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Non_state_promotion_gets_the_far_future_sentinel()
    {
        _backend.DistillJson = FactJson(
            statement: "I visited France in spring 2026.", kind: "experience",
            confidence: 0.9, horizon: "null");

        await BuildEngine().ProcessAsync(Episode);

        (_, MemoryMetadata meta) = Assert.Single(_memory.Stored);
        Assert.Equal(MemoryMetadata.FarFutureUnixSeconds, meta.ExpiresAt);
    }

    [Fact]
    public async Task Same_fact_against_active_reinforces()
    {
        _backend.DistillJson = FactJson();
        _memory.SearchResults.Add(
            Neighbor("m-active", "I'm recovering from wisdom tooth extraction.", 0.95, MemoryStatuses.Active));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Stored); // nothing new stored
        (string id, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Patches);
        Assert.Equal("m-active", id);
        Assert.Equal(1, patch["reinforced"]);
        Assert.Equal(0.7, (double)patch["confidence"]!, precision: 10); // 0.6 + bump
        Assert.Equal(MemoryUpdateReasons.Reinforced, patch["updated_reason"]);
        Assert.Equal("reinforced", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Same_fact_against_staged_promotes_it()
    {
        _backend.DistillJson = FactJson(confidence: 0.7);
        _memory.SearchResults.Add(
            Neighbor("m-staged", "I'm recovering from wisdom tooth extraction.", 0.95, MemoryStatuses.Staged));

        await BuildEngine().ProcessAsync(Episode);

        (string id, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Patches);
        Assert.Equal("m-staged", id);
        Assert.Equal(MemoryStatuses.Active, patch["status"]);
        Assert.Equal("promoted", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Committed_action_refines_an_active_intent_into_one_memory()
    {
        // The booking confirmation lands on the active "considering a flight" intent.
        _backend.DistillJson = FactJson(
            statement: "I booked a United flight to San Diego for Sep 9-13.",
            kind: "experience", confidence: 0.9, horizon: "null");
        _memory.SearchResults.Add(Neighbor(
            "m-active", "I'm considering booking a flight.", 0.95, MemoryStatuses.Active,
            confidence: 0.6, kind: "state"));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Stored); // merged into the existing memory, not a new row
        (string id, string? text, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Updates);
        Assert.Equal("m-active", id);
        Assert.Equal("I booked a United flight to San Diego for Sep 9-13.", text); // text adopted
        Assert.Equal("experience", patch["kind"]);                                 // kind adopted
        Assert.Equal(MemoryUpdateReasons.Revised, patch["updated_reason"]);
        Assert.Equal("revised", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Committed_action_refines_a_staged_intent_on_promotion()
    {
        _backend.DistillJson = FactJson(
            statement: "I booked a flight to San Diego for Sep 9-13.",
            kind: "experience", confidence: 0.9, horizon: "null");
        _memory.SearchResults.Add(Neighbor(
            "m-staged", "I'm considering booking a flight.", 0.8, MemoryStatuses.Staged,
            confidence: 0.6, kind: "state"));

        await BuildEngine().ProcessAsync(Episode);

        (string id, string? text, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Updates);
        Assert.Equal("m-staged", id);
        Assert.Equal(MemoryStatuses.Active, patch["status"]);
        Assert.Equal("I booked a flight to San Diego for Sep 9-13.", text); // adopted on promotion
        Assert.Equal("promoted", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Ordinary_reinforcement_keeps_the_existing_text()
    {
        // A non-committed supporting fact (below the high-signal bar) must NOT rewrite the text.
        _backend.DistillJson = FactJson(confidence: 0.7);
        _memory.SearchResults.Add(Neighbor(
            "m-active", "I'm recovering from wisdom tooth extraction.", 0.95, MemoryStatuses.Active,
            confidence: 0.6));

        await BuildEngine().ProcessAsync(Episode);

        (_, string? text, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Updates);
        Assert.Null(text); // text left as-is
        Assert.Equal(MemoryUpdateReasons.Reinforced, patch["updated_reason"]);
    }

    [Fact]
    public async Task Committed_action_never_rewrites_a_pinned_memory()
    {
        _backend.DistillJson = FactJson(
            statement: "I booked a flight.", kind: "experience", confidence: 0.9, horizon: "null");
        _memory.SearchResults.Add(Neighbor(
            "m-pinned", "I'm planning a flight.", 0.95, MemoryStatuses.Active,
            pinned: true, confidence: 0.6));

        await BuildEngine().ProcessAsync(Episode);

        (_, string? text, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Updates);
        Assert.Null(text);                                                    // pinned text untouched
        Assert.Equal(0.6, (double)patch["confidence"]!, precision: 10);       // and its confidence
        Assert.Equal(MemoryUpdateReasons.Reinforced, patch["updated_reason"]);
    }

    [Fact]
    public async Task Same_topic_against_staged_also_promotes_evidence_accumulates()
    {
        // A second supporting episode rarely words the fact identically; staged
        // candidates promote on the same-topic band, not the same-fact band.
        _backend.DistillJson = FactJson(
            statement: "My wisdom tooth removal is still healing.", confidence: 0.7);
        _memory.SearchResults.Add(
            Neighbor("m-staged", "I'm recovering from wisdom tooth extraction.", 0.8, MemoryStatuses.Staged));

        await BuildEngine().ProcessAsync(Episode);

        (string id, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Patches);
        Assert.Equal("m-staged", id);
        Assert.Equal(MemoryStatuses.Active, patch["status"]);
        Assert.Equal(0, _backend.ArbitrationCalls); // no arbitration for staged targets
    }

    [Fact]
    public async Task Staged_promotion_reconciles_into_an_existing_active_instead_of_duplicating()
    {
        // The candidate best-matches a staged near-duplicate, but an active memory already covers
        // the topic. Reconcile into the active and retire the staged — no parallel active row.
        _backend.DistillJson = FactJson(
            statement: "I'm planning a trip to San Diego for a large group.", confidence: 0.7);
        _backend.ArbitrationAnswer = "DUPLICATE";
        _memory.SearchResults.Add(Neighbor(
            "m-staged", "Planning a San Diego group trip.", 0.95, MemoryStatuses.Staged, confidence: 0.6));
        _memory.SearchResults.Add(Neighbor(
            "m-active", "I'm planning a trip to San Diego for a large group (9-10 adults).", 0.82,
            MemoryStatuses.Active, confidence: 0.8));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Stored); // no parallel memory created
        Assert.Equal(
            MemoryUpdateReasons.Reinforced,
            _memory.Patches.Single(p => p.Id == "m-active").Patch["updated_reason"]);
        (string _, IReadOnlyDictionary<string, object?> stagedPatch) =
            _memory.Patches.Single(p => p.Id == "m-staged");
        Assert.Equal(MemoryStatuses.Archived, stagedPatch["status"]);
        Assert.Equal(MemoryUpdateReasons.Deduped, stagedPatch["updated_reason"]);
        List<string> actions = (await Decisions()).Select(d => d.Action).ToList();
        Assert.Contains("reinforced", actions);
        Assert.Contains("deduped", actions);
        Assert.Equal(1, _backend.ArbitrationCalls);
    }

    [Fact]
    public async Task Staged_promotes_normally_when_the_active_neighbor_is_a_distinct_topic()
    {
        // The staged candidate is the real match; the active neighbor is a genuinely different fact.
        _backend.DistillJson = FactJson(statement: "I'm planning a trip to San Diego.", confidence: 0.7);
        _backend.ArbitrationAnswer = "COEXIST";
        _memory.SearchResults.Add(Neighbor(
            "m-staged", "Planning a San Diego trip.", 0.95, MemoryStatuses.Staged, confidence: 0.6));
        _memory.SearchResults.Add(Neighbor(
            "m-active", "I rented a car in San Diego last year.", 0.80, MemoryStatuses.Active,
            confidence: 0.8, kind: "experience"));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Equal(
            MemoryStatuses.Active, _memory.Patches.Single(p => p.Id == "m-staged").Patch["status"]);
        Assert.DoesNotContain(_memory.Patches, p => p.Id == "m-active"); // distinct active untouched
        Assert.DoesNotContain(await Decisions(), d => d.Action == "deduped");
        Assert.Equal(1, _backend.ArbitrationCalls);
    }

    [Fact]
    public async Task Same_topic_supersedes_archives_old_and_stores_revision()
    {
        _backend.DistillJson = FactJson(
            statement: "My mouth has healed; I'm off soft foods.", confidence: 0.7);
        _backend.ArbitrationAnswer = "SUPERSEDES";
        _memory.SearchResults.Add(
            Neighbor("m-old", "I'm recovering from wisdom tooth extraction.", 0.8, MemoryStatuses.Active));

        await BuildEngine().ProcessAsync(Episode);

        (string id, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Patches);
        Assert.Equal("m-old", id);
        Assert.Equal(MemoryStatuses.Archived, patch["status"]);
        (_, MemoryMetadata meta) = Assert.Single(_memory.Stored);
        Assert.Equal(MemoryStatuses.Active, meta.Status);
        Assert.Equal("m-old", meta.Supersedes);
        Assert.Contains(await Decisions(), d => d.Action == "revised");
    }

    [Fact]
    public async Task Same_topic_duplicate_reinforces_instead()
    {
        _backend.DistillJson = FactJson(confidence: 0.7);
        _backend.ArbitrationAnswer = "DUPLICATE";
        _memory.SearchResults.Add(
            Neighbor("m-active", "Recovering from a wisdom tooth removal.", 0.8, MemoryStatuses.Active));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Stored);
        Assert.Equal("m-active", Assert.Single(_memory.Patches).Id);
        Assert.Equal("reinforced", (await Decisions())[0].Action);
    }

    [Fact]
    public async Task Same_topic_coexist_stages_the_candidate()
    {
        _backend.DistillJson = FactJson(
            statement: "I have a dentist follow-up next month.", confidence: 0.7);
        _backend.ArbitrationAnswer = "COEXIST";
        _memory.SearchResults.Add(
            Neighbor("m-active", "I'm recovering from wisdom tooth extraction.", 0.8, MemoryStatuses.Active));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Patches);
        (_, MemoryMetadata meta) = Assert.Single(_memory.Stored);
        Assert.Equal(MemoryStatuses.Staged, meta.Status);
    }

    [Fact]
    public async Task Supersedes_against_pinned_memory_asks_instead_of_acting()
    {
        _backend.DistillJson = FactJson(statement: "I moved to Austin.", kind: "identity", confidence: 0.7, horizon: "null");
        _backend.ArbitrationAnswer = "SUPERSEDES";
        _memory.SearchResults.Add(
            Neighbor("m-pinned", "I live in Boston.", 0.8, MemoryStatuses.Active, pinned: true, kind: "identity"));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Patches); // never auto-archives user-authority memories
        Assert.Empty(_memory.Stored);
        DecisionEntry decision = (await Decisions())[0];
        Assert.Equal("needs_confirmation", decision.Action);
        Assert.Equal("m-pinned", decision.MemoryId);
    }

    [Fact]
    public async Task Arbitration_failure_degrades_to_staging()
    {
        _backend.DistillJson = FactJson(confidence: 0.7);
        _backend.ThrowOnArbitration = true;
        _memory.SearchResults.Add(
            Neighbor("m-active", "Recovering from wisdom teeth.", 0.8, MemoryStatuses.Active));

        await BuildEngine().ProcessAsync(Episode);

        Assert.Empty(_memory.Patches);
        Assert.Equal(MemoryStatuses.Staged, Assert.Single(_memory.Stored).Metadata.Status);
    }

    [Fact]
    public async Task Budget_defers_promotion_but_never_drops()
    {
        // Exhaust today's budget with prior 'promoted' decisions.
        for (int i = 0; i < TestDailyBudget; i++)
        {
            await _activity.LogDecisionAsync(new DecisionEntry(
                _time.Now.AddHours(-1), $"ep-{i}", "promoted", "seed", "s", "state", $"m{i}"));
        }

        _backend.DistillJson = FactJson(confidence: 0.7);
        _memory.SearchResults.Add(
            Neighbor("m-staged", "I'm recovering from wisdom tooth extraction.", 0.95, MemoryStatuses.Staged));

        await BuildEngine().ProcessAsync(Episode);

        (string id, IReadOnlyDictionary<string, object?> patch) = Assert.Single(_memory.Patches);
        Assert.Equal("m-staged", id);
        Assert.False(patch.ContainsKey("status")); // still staged — only TTL extended
        Assert.True(patch.ContainsKey("expires_at"));
        Assert.Contains(await Decisions(), d => d.Action == "deferred");
    }

    [Fact]
    public async Task Budget_resets_at_local_midnight()
    {
        for (int i = 0; i < TestDailyBudget; i++)
        {
            await _activity.LogDecisionAsync(new DecisionEntry(
                _time.Now.AddHours(-1), $"ep-{i}", "promoted", "seed", "s", "state", $"m{i}"));
        }

        _time.Now = _time.Now.AddDays(1); // yesterday's promotions no longer count
        _backend.DistillJson = FactJson(confidence: 0.9);

        await BuildEngine().ProcessAsync(Episode);

        Assert.Equal(MemoryStatuses.Active, Assert.Single(_memory.Stored).Metadata.Status);
    }


    [Fact]
    public async Task Budget_survives_UTC_rollover_inside_the_local_day()
    {
        // 21:00 local on the 15th is 02:00 UTC on the 16th (the fake clock is UTC-5): UTC has
        // rolled over, the user's day has not. Under the old UTC boundary the budget silently
        // refilled mid-evening — which is exactly what made the app's "today" counters reset
        // at 8pm and the evening's captures appear to vanish.
        _time.Now = new DateTimeOffset(2026, 7, 16, 2, 0, 0, TimeSpan.Zero);
        var earlierSameLocalDay = new DateTimeOffset(2026, 7, 15, 15, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < TestDailyBudget; i++)
        {
            await _activity.LogDecisionAsync(new DecisionEntry(
                earlierSameLocalDay, $"ep-{i}", "promoted", "seed", "s", "state", $"m{i}"));
        }

        _backend.DistillJson = FactJson(confidence: 0.9);

        await BuildEngine().ProcessAsync(Episode);

        Assert.Equal(MemoryStatuses.Staged, Assert.Single(_memory.Stored).Metadata.Status);
        Assert.Contains(
            await Decisions(),
            d => d.Action.StartsWith("deferred", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Distill_failure_and_empty_answers_write_their_decisions()
    {
        _backend.DistillJson = "not json at all";
        await BuildEngine().ProcessAsync(Episode);
        Assert.Equal("distill_failed", (await Decisions())[0].Action);

        _backend.DistillJson = """{"facts": []}""";
        await BuildEngine().ProcessAsync(Episode);
        Assert.Equal("no_facts", (await Decisions())[0].Action);
        Assert.Empty(_memory.Stored);
    }

    [Fact]
    public async Task Match_search_targets_live_rows_only()
    {
        _backend.DistillJson = FactJson();

        await BuildEngine().ProcessAsync(Episode);

        Assert.NotNull(_memory.LastSearchFilters);
        var statusOp = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            _memory.LastSearchFilters!["status"]);
        Assert.Contains("in", statusOp.Keys);
        Assert.Contains("expires_at", _memory.LastSearchFilters.Keys);
    }
}
