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
/// client must be able to trust that whatever comes back is worth context space.</summary>
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
    /// <paramref name="query"/>, optionally restricted to <paramref name="kinds"/>.</summary>
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

        IReadOnlyList<MemoryRecord> candidates = await _memory.SearchAsync(
            query, UserId, take * _options.OverFetchMultiplier, filters, cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Select(record =>
            {
                MemoryMetadata? metadata = MemoryMetadata.From(record);
                double score = RecallScorer.Blend(record.Score ?? 0.0, metadata, now, _options);
                return (Record: record, Metadata: metadata, Blended: score);
            })
            .Where(hit => hit.Blended >= _options.Floor)
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
