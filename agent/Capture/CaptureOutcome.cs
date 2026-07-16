namespace Lore.Agent.Capture;

/// <summary>What the loop did with one capture candidate — the explicit result of one trip
/// through extract → filter → gate → analyze → store.</summary>
public enum CaptureOutcome
{
    /// <summary>Dropped by the sensitivity filter.</summary>
    Filtered,

    /// <summary>Skipped by the smart gate (unchanged, duplicate, heartbeat).</summary>
    Skipped,

    /// <summary>The model produced no usable observation.</summary>
    AnalysisEmpty,

    /// <summary>The observation couldn't be stored (memoryd unavailable) — logged and
    /// recoverable; the loop continues.</summary>
    MemoryError,

    /// <summary>A memory was stored.</summary>
    Captured,

    /// <summary>v2 pipeline: the observation joined (or opened) the current episode.</summary>
    Observed,

    /// <summary>v2 pipeline: the observation closed an episode, which was persisted and
    /// handed to the episode processor.</summary>
    EpisodeClosed,
}
