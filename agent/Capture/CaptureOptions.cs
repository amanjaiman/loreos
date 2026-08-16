namespace Lore.Agent.Capture;

/// <summary>How the capture loop runs, bound from the <c>capture</c> config section.
/// Episode/lifecycle thresholds live in the nested options; the blocklist is the user's
/// own apps and keywords.</summary>
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
    /// within this window are caught at the next re-examination.</summary>
    public TimeSpan RecaptureInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Episode segmentation thresholds (v2 pipeline).</summary>
    public Episodes.EpisodeOptions Episodes { get; init; } = new();

    /// <summary>Lifecycle routing thresholds and the daily promotion budget (v2 pipeline).</summary>
    public Lifecycle.LifecycleOptions Lifecycle { get; init; } = new();

    /// <summary>How many days of <b>evidence</b> — episodes, decisions, activity log — are kept
    /// before <see cref="Storage.RetentionService"/> prunes them (v2-008 R4.1). <c>0</c> (or any
    /// negative value) means keep forever, for users who want it.
    ///
    /// <para>Memories are never subject to this. Retention deletes the evidence, not what the
    /// evidence supported — a memory whose supporting episodes have aged out stays recallable,
    /// and its evidence view simply shows what survives.</para></summary>
    public int RetentionDays { get; init; } = 90;

    /// <summary>Opt-in troubleshooting switch: record the text each capture actually read into
    /// <c>raw_captures</c> (v2-008 R4.2). <b>Off by default</b>, because always-on it is
    /// 5–15 MB/day for a debugging table, and because <c>episodes</c> + <c>decisions</c> already
    /// answer most tuning questions — this one exists for "why isn't Lore seeing this app?".
    ///
    /// <para>Only post-filter text is ever written, and the table is hard-bounded to 24 hours or
    /// 500 rows. With this off nothing is written and <c>GET /recent</c> reports empty.</para>
    ///
    /// <para>Bound at startup, so a change takes effect on the next agent start — unlike the
    /// pause and blocklist choices, which are live. That is the right trade for a diagnostic the
    /// user turns on deliberately when they sit down to debug something.</para></summary>
    public bool Diagnostics { get; init; }

    /// <summary>Executables the user never wants captured.</summary>
    public IReadOnlyList<string> BlocklistApps { get; init; } = [];

    /// <summary>Keywords that drop a capture when found in a title or text.</summary>
    public IReadOnlyList<string> BlocklistKeywords { get; init; } = [];
}
