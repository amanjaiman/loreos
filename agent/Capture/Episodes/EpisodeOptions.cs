namespace Lore.Agent.Capture.Episodes;

/// <summary>Episode segmentation thresholds (v2-001), bound from the
/// <c>capture.episodes</c> config section. Thresholds are config, not code — bad splits
/// are tuned, not patched (spec risk: segmentation is heuristic).</summary>
public sealed class EpisodeOptions
{
    /// <summary>Max gap between observations for the newer one to join the open episode;
    /// a longer gap breaks continuity and closes the episode.</summary>
    public TimeSpan ContinuityGap { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>With no new observations for this long, the open episode closes (checked
    /// each capture tick) so distillation is never postponed indefinitely.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>An episode never spans longer than this, related or not.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(45);

    /// <summary>An episode never holds more observations than this.</summary>
    public int MaxObservations { get; init; } = 40;

    /// <summary>Title-token Jaccard at or above which two windows count as related.</summary>
    public double TitleSimilarityThreshold { get; init; } = 0.4;

    /// <summary>Text Jaccard at or above which two observations count as related.</summary>
    public double TextSimilarityThreshold { get; init; } = 0.5;

    /// <summary>Text Jaccard at or above which a new observation is a near-duplicate of
    /// the episode's latest — counted, but not stored as another sample.</summary>
    public double DuplicateThreshold { get; init; } = 0.9;

    /// <summary>Most representative text samples kept per episode (distiller input bound).</summary>
    public int MaxSamples { get; init; } = 8;

    /// <summary>Each stored sample is truncated to this many characters.</summary>
    public int SampleMaxChars { get; init; } = 600;
}
