using Lore.Agent.Memory;

namespace Lore.Agent.Recall;

/// <summary>One recalled memory, ready for a client's context window.</summary>
public sealed record RecallHit(
    string Id,
    string Statement,
    string Kind,
    long EstablishedAt,
    double Score);

/// <summary>The every-turn recall path (v2-001): one filtered vector search + local
/// scoring math, zero generative calls. Clients call this on every user message, so the
/// only model-shaped work is the query embedding inside the store's search; everything
/// else here is arithmetic. Below-floor matches return empty rather than padding — a
/// client must be able to trust that whatever comes back is worth context space.
///
/// <para><b>Inclusion and ranking are two different questions, scored differently</b>
/// (v2-008 R5.3). "Is this memory relevant to the query?" is a purely semantic question,
/// so <see cref="RecallOptions.Floor"/> is compared against the raw semantic score.
/// "Where should it sit in the list?" is what <see cref="RecallScorer.Blend"/> is for, so
/// the blend — semantic × kind × temporal × confidence — decides order only.</para>
///
/// <para>These were one comparison until v2-008, and conflating them let <i>age</i> make a
/// <i>relevant</i> memory invisible. The blend's temporal factor saturates at
/// <see cref="RecallOptions.ExperienceDecayFloor"/> (0.6) once an <c>experience</c> is
/// ~187 days old, so past six months the best any experience could blend was
/// <c>semantic × 0.9 × 0.6 × confidence</c>. Against a floor of 0.47 that needs a semantic
/// score of 0.87 at perfect confidence, and at confidence ≤ 0.87 it is arithmetically
/// impossible at <i>any</i> similarity. Real same-topic similarity is ~0.82, so every
/// experience older than roughly six months was unrecallable — the exact opposite of the
/// invariant <see cref="RecallScorer"/> documents ("experiences fade gently with age but
/// never vanish"). The decay floor exists to guarantee that memories never disappear; the
/// recall floor sat above where that guarantee lands and silently cancelled it.</para>
///
/// <para><b>Do not "simplify" this back to one comparison.</b> Ordering by the blend while
/// filtering on the blend looks redundant and is not: an aged booking now returns on its
/// 0.82 semantic relevance and ranks last on its 0.44 blend, which is the intended
/// behaviour. Lowering <see cref="RecallOptions.Floor"/> is not an equivalent fix — that
/// value is calibrated to keep unrelated pairs out, and unrelated pairs sit at ~0.42
/// semantic, just under the same 0.47 threshold. The floor still does the job it was
/// calibrated for; it is now applied to the quantity it was actually measuring.</para>
///
/// <para>The consequence to keep in mind when reading results: confidence and recency no
/// longer gate inclusion for <i>any</i> kind — a low-confidence hunch that is genuinely
/// on-topic now returns, carrying a correspondingly low score and a correspondingly low
/// rank. Callers weigh a hit by its score; they no longer get that weighing for free as
/// an omission.</para></summary>
public sealed class RecallService
{
    private const string UserId = "default";

    private readonly IMemoryService _memory;
    private readonly RecallOptions _options;
    private readonly TimeProvider _time;

    public RecallService(IMemoryService memory, RecallOptions options, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        _memory = memory;
        _options = options;
        _time = time;
    }

    /// <summary>Recall at most <paramref name="k"/> memories relevant to
    /// <paramref name="query"/>, optionally restricted to <paramref name="kinds"/>.
    /// Relevance (which memories come back) is decided by the semantic score against
    /// <see cref="RecallOptions.Floor"/>; strength (their order, and the
    /// <see cref="RecallHit.Score"/> each carries) is decided by the blend. See the type
    /// remarks for why those are deliberately two different numbers.</summary>
    public async Task<IReadOnlyList<RecallHit>> RecallAsync(
        string query,
        int? k = null,
        IReadOnlyList<string>? kinds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        int take = k is > 0 ? k.Value : _options.DefaultK;
        long now = _time.GetUtcNow().ToUnixTimeSeconds();

        Dictionary<string, object?> filters = MemoryFilters.ActiveUnexpired(now);
        if (kinds is { Count: > 0 })
        {
            filters["kind"] = MemoryFilters.In(kinds);
        }

        // Over-fetch: the store returns its top-N by semantic score, and the floor is now
        // applied to that same quantity, so this candidate pool is exactly the right one —
        // nothing the store ranked out could have cleared the floor. The multiplier buys
        // headroom for the blend to reorder within the pool.
        IReadOnlyList<MemoryRecord> candidates = await _memory.SearchAsync(
            query, UserId, take * _options.OverFetchMultiplier, filters, cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Select(record =>
            {
                MemoryMetadata? metadata = MemoryMetadata.From(record);
                double semantic = record.Score ?? 0.0;
                return (
                    Record: record,
                    Metadata: metadata,
                    Semantic: semantic,
                    Blended: RecallScorer.Blend(semantic, metadata, now, _options));
            })
            // Inclusion is a relevance question, so it reads the semantic score alone.
            // The null-metadata check is load-bearing and cannot be dropped: rows not yet
            // migrated to v2 metadata used to be excluded implicitly, because Blend
            // returns 0.0 for them and 0.0 never cleared the floor. The semantic score
            // carries no such signal — an unmigrated row can be highly similar — so the
            // exclusion has to be stated outright. It also guards the Metadata! below.
            .Where(hit => hit.Metadata is not null && hit.Semantic >= _options.Floor)
            // Ranking is a different question, and it is what the blend answers: kind
            // weight, recency decay, and confidence decide the order of what got in.
            .OrderByDescending(hit => hit.Blended)
            .Take(take)
            .Select(hit => new RecallHit(
                hit.Record.Id,
                hit.Record.Memory,
                hit.Metadata!.Kind,
                hit.Metadata.EstablishedAt,
                Math.Round(hit.Blended, 4)))
            .ToArray();
    }
}
