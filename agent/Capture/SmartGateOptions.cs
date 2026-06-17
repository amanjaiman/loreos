namespace Lore.Agent.Capture;

/// <summary>The v1 smart-capture thresholds that tune how aggressively the gate skips
/// low-signal captures (constitution NFR: inference proportional to genuine activity, not
/// wall-clock time). Defaults are sensible starting points; T008 binds them from config.
///
/// <para>Similarity is the token-set ratio from <see cref="TextSimilarity"/> in [0, 1].
/// The high/low pair frames a decision band: at/above <see cref="DiffThresholdHigh"/> a
/// capture is unchanged and skipped; at/below <see cref="DiffThresholdLow"/> it has
/// changed enough to capture; in between, the per-content-type threshold decides.</para>
/// </summary>
public sealed class SmartGateOptions
{
    /// <summary>At or above this similarity, content counts as unchanged → skip.</summary>
    public double DiffThresholdHigh { get; init; } = 0.90;

    /// <summary>At or below this similarity, content has changed enough → capture.</summary>
    public double DiffThresholdLow { get; init; } = 0.50;

    /// <summary>Similarity bar for reading content in the ambiguous band — higher than the
    /// default so scrolling within the same article doesn't re-trigger capture.</summary>
    public double ReadingThreshold { get; init; } = 0.85;

    /// <summary>Similarity bar for shopping content in the ambiguous band.</summary>
    public double ShoppingThreshold { get; init; } = 0.80;

    /// <summary>For messaging, only the trailing N characters are compared (new messages
    /// append to the end; old conversation stays constant).</summary>
    public int MessagingTailChars { get; init; } = 200;

    /// <summary>Minimum time between captures of the same coding window — editors update
    /// constantly, so re-captures within this interval are heartbeat noise.</summary>
    public TimeSpan CodingHeartbeat { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>How recently a near-identical capture must have happened to suppress a
    /// duplicate regardless of which window it came from.</summary>
    public TimeSpan RecentDuplicateWindow { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cap on the recent-capture history the gate keeps.</summary>
    public int MaxCaptureHistory { get; init; } = 20;
}
