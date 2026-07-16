using System.Threading;

namespace Lore.Agent.Capture;

/// <summary>A point-in-time count of every capture decision the loop made.</summary>
public sealed record CaptureMetricsSnapshot(
    long Captured,
    long Observed,
    long EpisodesClosed,
    long AnalysisEmpty,
    long MemoryErrors,
    IReadOnlyDictionary<FilterReason, long> Filtered,
    IReadOnlyDictionary<SkipReason, long> Skipped)
{
    /// <summary>Total captures dropped by the sensitivity filter.</summary>
    public long TotalFiltered => Filtered.Values.Sum();

    /// <summary>Total captures skipped by the smart gate.</summary>
    public long TotalSkipped => Skipped.Values.Sum();
}

/// <summary>Live counters of why the loop captured, skipped, or filtered each window — so
/// the user can see why Lore did or didn't record something (acceptance criterion 2/3, and
/// what the local API surfaces in 005). Thread-safe: the loop writes, a future API reads.</summary>
public sealed class CaptureMetrics
{
    private readonly long[] _filtered = new long[Enum.GetValues<FilterReason>().Length];
    private readonly long[] _skipped = new long[Enum.GetValues<SkipReason>().Length];
    private long _captured;
    private long _observed;
    private long _episodesClosed;
    private long _analysisEmpty;
    private long _memoryErrors;

    /// <summary>v1: a memory was stored.</summary>
    public void Captured() => Interlocked.Increment(ref _captured);

    /// <summary>v2: a post-filter observation entered the episode stream.</summary>
    public void Observed() => Interlocked.Increment(ref _observed);

    /// <summary>v2: an episode closed and was persisted.</summary>
    public void EpisodeClosed() => Interlocked.Increment(ref _episodesClosed);

    public void AnalysisEmpty() => Interlocked.Increment(ref _analysisEmpty);

    public void MemoryError() => Interlocked.Increment(ref _memoryErrors);

    public void Filtered(FilterReason reason) => Interlocked.Increment(ref _filtered[(int)reason]);

    public void Skipped(SkipReason reason) => Interlocked.Increment(ref _skipped[(int)reason]);

    /// <summary>A consistent-enough snapshot of all counters for display.</summary>
    public CaptureMetricsSnapshot Snapshot() => new(
        Interlocked.Read(ref _captured),
        Interlocked.Read(ref _observed),
        Interlocked.Read(ref _episodesClosed),
        Interlocked.Read(ref _analysisEmpty),
        Interlocked.Read(ref _memoryErrors),
        ToMap<FilterReason>(_filtered),
        ToMap<SkipReason>(_skipped));

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
