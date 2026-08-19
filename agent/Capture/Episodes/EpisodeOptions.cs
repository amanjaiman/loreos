namespace Lore.Agent.Capture.Episodes;

/// <summary>Episode segmentation thresholds (v2-001), bound from the
/// <c>capture.episodes</c> config section. Thresholds are config, not code — bad splits
/// are tuned, not patched (spec risk: segmentation is heuristic).
///
/// <para>A record, not a plain class, since v2-008 R2: these values now ride in the live
/// <see cref="CaptureSnapshot"/> and are replaced wholesale on <c>PATCH /config</c>, so the
/// snapshot needs a cheap <c>with</c> to repair one out-of-range field without restating
/// every other one (and silently dropping any added later).</para></summary>
public sealed record EpisodeOptions
{
    /// <summary>Max gap between observations for the newer one to join the open episode;
    /// a longer gap breaks continuity and closes the episode.</summary>
    public TimeSpan ContinuityGap { get; init; } = TimeSpan.FromMinutes(3);

    /// <summary>With no new observations for this long, the open episode closes (checked
    /// each capture tick) so distillation is never postponed indefinitely.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>An episode never spans longer than this, related or not.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromMinutes(45);

    /// <summary>An episode never holds more observations than this.
    ///
    /// <para>48, not the 40 shipped before v2-008. This — not the clock — is what ends most
    /// episodes, so it must scale with <see cref="CaptureOptions.RecaptureInterval"/> (30s → 25s
    /// in the same change) or the attentiveness control would change episode <em>length</em>
    /// rather than capture density, roughly doubling AI spend at the close stop. The
    /// 30/48/80 ↔ 40s/25s/15s pairings hold every stop at a ~20-minute episode.</para></summary>
    public int MaxObservations { get; init; } = 48;

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
