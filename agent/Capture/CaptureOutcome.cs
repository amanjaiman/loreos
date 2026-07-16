namespace Lore.Agent.Capture;

/// <summary>What the loop did with one capture candidate — the explicit result of one
/// trip through extract → filter → episode intake.</summary>
public enum CaptureOutcome
{
    /// <summary>Dropped by the sensitivity filter.</summary>
    Filtered,

    /// <summary>The observation joined (or opened) the current episode.</summary>
    Observed,

    /// <summary>The observation closed an episode, which was persisted and handed to
    /// the episode processor.</summary>
    EpisodeClosed,
}
