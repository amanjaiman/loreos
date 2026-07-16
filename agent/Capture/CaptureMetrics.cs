using System.Threading;

namespace Lore.Agent.Capture;

/// <summary>A point-in-time count of every capture decision the loop made.</summary>
public sealed record CaptureMetricsSnapshot(
    long Observed,
    long EpisodesClosed,
    IReadOnlyDictionary<FilterReason, long> Filtered)
{
    /// <summary>Total captures dropped by the sensitivity filter.</summary>
    public long TotalFiltered => Filtered.Values.Sum();
}

/// <summary>Live counters of why the loop observed or filtered each window — so the
/// user can see why Lore did or didn't look at something. Memory-level decisions
/// (staged/promoted/…) live in the decision trail, not here. Thread-safe: the loop
/// writes, the API reads.</summary>
public sealed class CaptureMetrics
{
    private readonly long[] _filtered = new long[Enum.GetValues<FilterReason>().Length];
    private long _observed;
    private long _episodesClosed;

    /// <summary>A post-filter observation entered the episode stream.</summary>
    public void Observed() => Interlocked.Increment(ref _observed);

    /// <summary>An episode closed and was persisted.</summary>
    public void EpisodeClosed() => Interlocked.Increment(ref _episodesClosed);

    public void Filtered(FilterReason reason) => Interlocked.Increment(ref _filtered[(int)reason]);

    /// <summary>A consistent-enough snapshot of all counters for display.</summary>
    public CaptureMetricsSnapshot Snapshot() => new(
        Interlocked.Read(ref _observed),
        Interlocked.Read(ref _episodesClosed),
        ToMap<FilterReason>(_filtered));

    private static Dictionary<TReason, long> ToMap<TReason>(long[] counters)
        where TReason : struct, Enum
    {
        var map = new Dictionary<TReason, long>();
        foreach (TReason reason in Enum.GetValues<TReason>())
        {
            long value = Interlocked.Read(ref counters[Convert.ToInt32(reason, null)]);
            if (value > 0)
            {
                map[reason] = value;
            }
        }

        return map;
    }
}
