using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Inference;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Lifecycle;

/// <summary>The v2-001 lifecycle engine: routes each distilled candidate fact against the
/// existing store — reinforce, promote, arbitrate (revise), stage, or defer — and writes
/// every step to the decision trail. This is where reconciliation lives now (the raw
/// store never merges); LLM arbitration runs at capture time only. Implements
/// <see cref="IEpisodeProcessor"/> as the tail of the capture loop; per that contract it
/// never throws for ordinary failures.</summary>
public sealed class LifecycleEngine : IEpisodeProcessor
{
    private const string UserId = "default";

    private readonly Distiller _distiller;
    private readonly IMemoryService _memory;
    private readonly ActivityStore _activity;
    private readonly IInferenceBackend _backend;
    private readonly LiveCaptureSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<LifecycleEngine> _logger;

    public LifecycleEngine(
        Distiller distiller,
        IMemoryService memory,
        ActivityStore activity,
        IInferenceBackend backend,
        LiveCaptureSettings settings,
        TimeProvider time,
        ILogger<LifecycleEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(distiller);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _distiller = distiller;
        _memory = memory;
        _activity = activity;
        _backend = backend;
        _settings = settings;
        _time = time;
        _logger = logger;
    }

    /// <summary>The routing thresholds in force right now (v2-008 R2), so the certainty control
    /// applies to the next candidate rather than the next agent start. Every use is an independent
    /// comparison against the store, so a snapshot swap between two of them can only change where
    /// a single candidate lands — it can never leave a memory half-written or disturb an episode,
    /// which is already off the builder and persisted by the time routing begins.</summary>
    private LifecycleOptions Options => _settings.Current.Lifecycle;

    public async Task ProcessAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);

        IReadOnlyList<CandidateFact>? facts = await _distiller
            .DistillAsync(episode, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            await LogAsync(episode.Id, "distill_failed", "model output unusable", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (facts.Count == 0)
        {
            await LogAsync(episode.Id, "no_facts", "episode revealed nothing durable", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        foreach (CandidateFact fact in facts)
        {
            try
            {
                await RouteAsync(episode, fact, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // one bad candidate never blocks the rest (criterion 6)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "lifecycle routing failed for a candidate of episode {Id}", episode.Id);
                await LogAsync(
                    episode.Id, "route_failed", ex.GetType().Name, fact,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task RouteAsync(Episode episode, CandidateFact fact, CancellationToken ct)
    {
        long now = _time.GetUtcNow().ToUnixTimeSeconds();

        // Match against live memories and staged candidates (dead staged rows are
        // excluded by their expiry).
        IReadOnlyList<MemoryRecord> neighbors = await _memory.SearchAsync(
            fact.Statement,
            UserId,
            Options.MatchNeighbors,
            new Dictionary<string, object?>
            {
                ["status"] = MemoryFilters.In([MemoryStatuses.Staged, MemoryStatuses.Active]),
                ["expires_at"] = MemoryFilters.Gt(now),
            },
            ct).ConfigureAwait(false);

        MemoryRecord? best = neighbors.Count > 0 ? neighbors[0] : null;
        double bestScore = best?.Score ?? 0.0;
        MemoryMetadata? bestMeta = best is null ? null : MemoryMetadata.From(best);

        if (best is not null && bestMeta is not null)
        {
            if (bestMeta.Status == MemoryStatuses.Active && bestScore >= Options.SameFactThreshold)
            {
                await ReinforceAsync(episode, fact, best, bestMeta, now, ct).ConfigureAwait(false);
                return;
            }

            // A staged candidate promotes on same-TOPIC support: staging exists to
            // accumulate evidence, and a second episode rarely words the fact
            // identically — requiring the same-fact band here would stall promotion.
            if (bestMeta.Status == MemoryStatuses.Staged && bestScore >= Options.SameTopicThreshold)
            {
                // Duplicate guard: a staged candidate can outscore an ACTIVE memory that already
                // covers the same topic (a tentative wording matches a later episode more closely
                // than the fuller, established memory). Promoting it as-is spawns a parallel active
                // row — the bug that split one San Diego trip into two. So first reconcile against
                // any active memory on the topic; if arbitration agrees it's the same fact, fold
                // into that memory and retire the staged duplicate. If the active is genuinely
                // distinct (coexist), the staged candidate is the real match — promote it.
                MemoryRecord? activeMatch = FindActive(neighbors, Options.SameTopicThreshold);
                if (activeMatch is not null
                    && await ReconcileWithActiveAsync(episode, fact, activeMatch, now, ct).ConfigureAwait(false))
                {
                    await DedupeStagedAsync(episode, fact, best, activeMatch.Id, ct).ConfigureAwait(false);
                    return;
                }

                await PromoteStagedAsync(episode, fact, best, bestMeta, now, ct).ConfigureAwait(false);
                return;
            }

            if (bestMeta.Status == MemoryStatuses.Active && bestScore >= Options.SameTopicThreshold)
            {
                if (await ReconcileWithActiveAsync(episode, fact, best, now, ct).ConfigureAwait(false))
                {
                    return;
                }

                // Coexist: genuinely distinct from the active neighbor — record it as new.
                await StageAsync(episode, fact, now, ct).ConfigureAwait(false);
                return;
            }
        }

        if (fact.Confidence >= Options.HighSignalConfidence)
        {
            // Committed action (booking, purchase, signed form): promote directly.
            await StoreActiveAsync(episode, fact, supersedes: string.Empty, now, ct).ConfigureAwait(false);
            return;
        }

        await StageAsync(episode, fact, now, ct).ConfigureAwait(false);
    }

    private async Task ReinforceAsync(
        Episode episode, CandidateFact fact, MemoryRecord target, MemoryMetadata meta, long now,
        CancellationToken ct)
    {
        // Pinned/user-edited memories still reinforce — a confidence bump never
        // contradicts user authority; only their text/lifecycle is untouchable.
        var episodes = new List<string>(meta.Episodes ?? []) { episode.Id };

        // A committed action landing on a memory that only recorded the INTENT rewrites that
        // memory to the committed fact — "planning to book a flight" and "booked a flight to
        // San Diego for Sep 9-13" collapse into one, instead of the stale intent lingering.
        bool userOwned = meta.UserEdited || meta.Pinned;
        bool refine = IsCommittedRefinement(fact, meta);

        double confidence;
        if (userOwned)
        {
            confidence = meta.Confidence;
        }
        else if (refine)
        {
            confidence = Math.Min(Options.ConfidenceCap, fact.Confidence);
        }
        else
        {
            confidence = Math.Min(Options.ConfidenceCap, meta.Confidence + Options.ReinforceBump);
        }

        long expiresAt;
        if (refine)
        {
            expiresAt = ExpiryFor(fact, now); // the refined fact's kind sets the new lifespan
        }
        else if (meta.Kind == MemoryKinds.State)
        {
            expiresAt = Math.Max(meta.ExpiresAt, now + HorizonSeconds(fact));
        }
        else
        {
            expiresAt = meta.ExpiresAt;
        }

        await _memory.UpdateAsync(
            target.Id,
            refine ? fact.Statement : null,
            new Dictionary<string, object?>
            {
                ["reinforced"] = meta.Reinforced + 1,
                ["confidence"] = confidence,
                ["kind"] = refine ? fact.Kind : meta.Kind,
                ["expires_at"] = expiresAt,
                ["episodes"] = episodes,
                ["updated_reason"] = refine ? MemoryUpdateReasons.Revised : MemoryUpdateReasons.Reinforced,
            },
            ct).ConfigureAwait(false);
        await LogAsync(
            episode.Id,
            refine ? "revised" : "reinforced",
            refine ? $"committed action refined '{Trim(target.Memory)}'" : $"score matched '{Trim(target.Memory)}'",
            fact, target.Id, ct).ConfigureAwait(false);
    }

    private async Task PromoteStagedAsync(
        Episode episode, CandidateFact fact, MemoryRecord staged, MemoryMetadata meta, long now,
        CancellationToken ct)
    {
        if (!await BudgetAllowsAsync(ct).ConfigureAwait(false))
        {
            // Deferred, not dropped: keep it staged with a fresh TTL so tomorrow's
            // budget can promote it.
            await _memory.PatchMetadataAsync(
                staged.Id,
                new Dictionary<string, object?> { ["expires_at"] = now + DaysToSeconds(Options.StagedTtlDays) },
                ct).ConfigureAwait(false);
            await LogAsync(episode.Id, "deferred", "daily promotion budget reached", fact, staged.Id, ct)
                .ConfigureAwait(false);
            return;
        }

        var episodes = new List<string>(meta.Episodes ?? []) { episode.Id };

        // If the episode that promotes the staged candidate is itself a committed action,
        // adopt its (more specific) statement so the promoted memory reflects what happened —
        // e.g. a staged "considering a flight to San Diego" promotes into "booked a flight to
        // San Diego for Sep 9-13" rather than staying at the tentative wording.
        bool refine = IsCommittedRefinement(fact, meta);
        await _memory.UpdateAsync(
            staged.Id,
            refine ? fact.Statement : null,
            new Dictionary<string, object?>
            {
                ["status"] = MemoryStatuses.Active,
                ["confidence"] = Math.Min(
                    Options.ConfidenceCap,
                    Math.Max(meta.Confidence, fact.Confidence) + Options.ReinforceBump),
                ["kind"] = refine ? fact.Kind : meta.Kind,
                ["expires_at"] = ExpiryFor(fact, now),
                ["established_at"] = now,
                ["episodes"] = episodes,
                ["updated_reason"] = MemoryUpdateReasons.Promoted,
            },
            ct).ConfigureAwait(false);
        await LogAsync(
            episode.Id, "promoted",
            refine ? "committed action promoted and refined the staged intent" : "second supporting episode",
            fact, staged.Id, ct).ConfigureAwait(false);
    }

    // The highest-scoring ACTIVE neighbor at or above <paramref name="threshold"/>, or null.
    // Neighbors arrive sorted by score, so the first active match is the strongest.
    private static MemoryRecord? FindActive(IReadOnlyList<MemoryRecord> neighbors, double threshold)
    {
        foreach (MemoryRecord neighbor in neighbors)
        {
            if ((neighbor.Score ?? 0.0) < threshold)
            {
                continue;
            }

            MemoryMetadata? meta = MemoryMetadata.From(neighbor);
            if (meta is not null && meta.Status == MemoryStatuses.Active)
            {
                return neighbor;
            }
        }

        return null;
    }

    // Reconcile a candidate into an existing ACTIVE memory: reinforce when it is clearly the same
    // fact, else let arbitration decide. Returns true when the candidate was absorbed (reinforced,
    // superseded, or surfaced for confirmation); false only when arbitration says the two coexist,
    // in which case the caller records the candidate separately.
    private async Task<bool> ReconcileWithActiveAsync(
        Episode episode, CandidateFact fact, MemoryRecord active, long now, CancellationToken ct)
    {
        MemoryMetadata meta = MemoryMetadata.From(active)!;
        if ((active.Score ?? 0.0) >= Options.SameFactThreshold)
        {
            await ReinforceAsync(episode, fact, active, meta, now, ct).ConfigureAwait(false);
            return true;
        }

        return await ArbitrateAsync(episode, fact, active, meta, now, ct).ConfigureAwait(false);
    }

    // Retire a staged candidate that duplicates a memory we just reconciled into an active row, so
    // the two never surface as parallel entries.
    private async Task DedupeStagedAsync(
        Episode episode, CandidateFact fact, MemoryRecord staged, string keptId, CancellationToken ct)
    {
        await _memory.PatchMetadataAsync(
            staged.Id,
            new Dictionary<string, object?>
            {
                ["status"] = MemoryStatuses.Archived,
                ["updated_reason"] = MemoryUpdateReasons.Deduped,
            },
            ct).ConfigureAwait(false);
        await LogAsync(
            episode.Id, "deduped",
            $"folded into the existing memory it duplicated ({keptId})", fact, staged.Id, ct)
            .ConfigureAwait(false);
    }

    // Decide how a candidate relates to a same-topic ACTIVE memory. Returns true when the candidate
    // was absorbed into <paramref name="target"/> (duplicate/supersede) or surfaced for the user's
    // confirmation; false when the verdict is coexist and the caller must record it separately.
    private async Task<bool> ArbitrateAsync(
        Episode episode, CandidateFact fact, MemoryRecord target, MemoryMetadata meta, long now,
        CancellationToken ct)
    {
        ArbitrationVerdict verdict;
        try
        {
            string? answer = await _backend
                .CompleteAsync(ArbitrationPrompt.Build(fact.Statement, target.Memory), ct)
                .ConfigureAwait(false);
            verdict = ArbitrationPrompt.ParseVerdict(answer);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // arbitration outage degrades to the safe verdict
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "arbitration call failed; treating as coexist");
            verdict = ArbitrationVerdict.Coexist;
        }

        switch (verdict)
        {
            case ArbitrationVerdict.Duplicate:
                await ReinforceAsync(episode, fact, target, meta, now, ct).ConfigureAwait(false);
                return true;

            case ArbitrationVerdict.Supersedes when meta.Pinned || meta.UserEdited:
                // User authority: never auto-archive their memory — surface it instead.
                await LogAsync(
                    episode.Id, "needs_confirmation",
                    $"new fact contradicts pinned/edited '{Trim(target.Memory)}'", fact, target.Id, ct)
                    .ConfigureAwait(false);
                return true;

            case ArbitrationVerdict.Supersedes:
                await _memory.PatchMetadataAsync(
                    target.Id,
                    new Dictionary<string, object?>
                    {
                        ["status"] = MemoryStatuses.Archived,
                        ["updated_reason"] = MemoryUpdateReasons.Revised,
                    },
                    ct).ConfigureAwait(false);
                await StoreActiveAsync(episode, fact, supersedes: target.Id, now, ct).ConfigureAwait(false);
                return true;

            default:
                return false; // coexist — the caller records the candidate separately
        }
    }

    // A revision replaces an archived memory (net memory count is flat), so it does not
    // consume budget; a direct high-signal store does.
    private async Task StoreActiveAsync(
        Episode episode, CandidateFact fact, string supersedes, long now, CancellationToken ct)
    {
        bool isRevision = supersedes.Length > 0;
        if (!isRevision && !await BudgetAllowsAsync(ct).ConfigureAwait(false))
        {
            await StageAsync(episode, fact, now, ct, "deferred_high_signal").ConfigureAwait(false);
            return;
        }

        var metadata = new MemoryMetadata(
            Kind: fact.Kind,
            Status: MemoryStatuses.Active,
            Confidence: Math.Min(Options.ConfidenceCap, fact.Confidence),
            ExpiresAt: ExpiryFor(fact, now),
            EstablishedAt: now,
            UpdatedReason: isRevision ? MemoryUpdateReasons.Revised : MemoryUpdateReasons.Promoted,
            Episodes: [episode.Id],
            Supersedes: supersedes);
        AddedMemory added = await _memory
            .StoreAsync(fact.Statement, metadata, UserId, ct).ConfigureAwait(false);
        await LogAsync(
            episode.Id,
            isRevision ? "revised" : "promoted",
            isRevision ? "supersedes an archived memory" : "high-signal episode",
            fact,
            added.Id,
            ct).ConfigureAwait(false);
    }

    private async Task StageAsync(
        Episode episode, CandidateFact fact, long now, CancellationToken ct,
        string action = "staged")
    {
        var metadata = new MemoryMetadata(
            Kind: fact.Kind,
            Status: MemoryStatuses.Staged,
            Confidence: fact.Confidence,
            ExpiresAt: now + DaysToSeconds(Options.StagedTtlDays),
            EstablishedAt: 0,
            UpdatedReason: MemoryUpdateReasons.Staged,
            Episodes: [episode.Id]);
        AddedMemory added = await _memory
            .StoreAsync(fact.Statement, metadata, UserId, ct).ConfigureAwait(false);
        await LogAsync(
            episode.Id, action, "awaiting a second supporting episode", fact, added.Id, ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> BudgetAllowsAsync(CancellationToken ct)
    {
        DateTimeOffset midnight = DayBoundary.StartOfToday(_time);
        int today = await _activity
            .CountDecisionsSinceAsync("promoted", midnight, ct).ConfigureAwait(false);
        return today < Options.DailyBudget;
    }

    // A matched candidate is a "committed refinement" of an existing memory when it is itself a
    // committed action (high-signal confidence) landing on a memory that still records only the
    // intent (below that bar). That is the signal to rewrite the memory to the committed fact
    // rather than merely reinforce the tentative one. User-authored memories are never rewritten.
    private bool IsCommittedRefinement(CandidateFact fact, MemoryMetadata meta) =>
        !meta.UserEdited
        && !meta.Pinned
        && fact.Confidence >= Options.HighSignalConfidence
        && meta.Confidence < Options.HighSignalConfidence;

    private long HorizonSeconds(CandidateFact fact) =>
        DaysToSeconds(Math.Clamp(
            fact.HorizonDays ?? Options.DefaultHorizonDays,
            Options.MinHorizonDays,
            Options.MaxHorizonDays));

    private long ExpiryFor(CandidateFact fact, long now) =>
        fact.Kind == MemoryKinds.State
            ? now + HorizonSeconds(fact)
            : MemoryMetadata.FarFutureUnixSeconds;

    private static long DaysToSeconds(int days) => days * 86_400L;

    private static string Trim(string text) =>
        text.Length <= 80 ? text : text[..80] + "…";

    private Task LogAsync(
        string episodeId, string action, string reason,
        CandidateFact? fact = null, string memoryId = "",
        CancellationToken cancellationToken = default) =>
        _activity.LogDecisionAsync(
            new DecisionEntry(
                _time.GetUtcNow(), episodeId, action, reason,
                fact?.Statement ?? string.Empty, fact?.Kind ?? string.Empty, memoryId),
            cancellationToken);
}
