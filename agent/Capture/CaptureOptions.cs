namespace Lore.Agent.Capture;

/// <summary>How the capture loop runs, bound from the <c>capture</c> config section. The
/// gate thresholds live in the nested <see cref="Gate"/>; the blocklist is the user's own
/// apps and keywords.</summary>
public sealed class CaptureOptions
{
    /// <summary>Master switch. When false the loop idles and captures nothing.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How often the foreground window is polled.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a window must hold focus before it is a capture candidate.</summary>
    public TimeSpan DwellThreshold { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>The minimum time before the same unchanged window is re-examined — bounds
    /// re-extraction so a window held in focus isn't re-read on every poll. Content changes
    /// within this window are caught at the next re-examination and judged by the gate.</summary>
    public TimeSpan RecaptureInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Which decision pipeline runs after the filter chain: <c>"v1"</c> is the
    /// dwell-gate-analyze path; <c>"v2"</c> is episode segmentation + skeptical
    /// distillation (v2-001). The default flips to v2 when the E2E harness is green
    /// (v2-001 T010); the v1 path is deleted by v2-002 after that.</summary>
    public string Pipeline { get; init; } = "v1";

    /// <summary>Episode segmentation thresholds (v2 pipeline).</summary>
    public Episodes.EpisodeOptions Episodes { get; init; } = new();

    /// <summary>Lifecycle routing thresholds and the daily promotion budget (v2 pipeline).</summary>
    public Lifecycle.LifecycleOptions Lifecycle { get; init; } = new();

    /// <summary>Executables the user never wants captured.</summary>
    public IReadOnlyList<string> BlocklistApps { get; init; } = [];

    /// <summary>Keywords that drop a capture when found in a title or text.</summary>
    public IReadOnlyList<string> BlocklistKeywords { get; init; } = [];

    /// <summary>Smart-gate thresholds.</summary>
    public SmartGateOptions Gate { get; init; } = new();
}
