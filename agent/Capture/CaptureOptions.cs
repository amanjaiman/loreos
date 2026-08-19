namespace Lore.Agent.Capture;

/// <summary>How the capture loop runs, bound from the <c>capture</c> config section.
/// Episode/lifecycle thresholds live in the nested options; the blocklist is the user's
/// own apps and keywords.
///
/// <para>Most of these are also <b>preset-derived</b> (v2-008 R3): the user picks
/// <see cref="Attentiveness"/>, <see cref="Certainty"/> and <see cref="Detail"/> and
/// <see cref="CapturePresets"/> turns those three names into numbers. A raw key explicitly
/// present in config.json wins over the preset for that field alone — every property here stays
/// individually settable for anyone who opens the file.</para>
///
/// <para>This type is the <b>startup seed only</b> (v2-008 R2). Nothing reads it at run time:
/// it is bound once, handed to <see cref="LiveCaptureSettings"/>, and from there every value
/// is served from the atomically-replaced <see cref="CaptureSnapshot"/> so a
/// <c>PATCH /config</c> applies without an agent restart. It is deliberately not registered in
/// DI — a second, frozen source of truth is exactly the stale-read bug R2 exists to remove.</para></summary>
public sealed record CaptureOptions
{
    /// <summary>Master switch. When false the loop idles and captures nothing.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>"How closely Lore watches" (v2-008 R1.1): <c>light</c> | <c>balanced</c> |
    /// <c>close</c>. Drives <see cref="PollInterval"/>, <see cref="DwellThreshold"/>,
    /// <see cref="RecaptureInterval"/>, <see cref="TitleRecaptureInterval"/> and the episode
    /// bounds. A <see langword="null"/>, empty or unrecognised name means <c>balanced</c>.
    ///
    /// <para>Deliberately a <see cref="string"/> rather than an enum: the configuration binder
    /// <b>throws</b> on an enum value it cannot parse, which would take the agent down at startup
    /// over a typo in a file the user is invited to edit. Parsing happens in
    /// <see cref="CapturePresets"/>, which repairs and logs instead.</para></summary>
    public string? Attentiveness { get; init; }

    /// <summary>"How sure Lore has to be" (v2-008 R1.2): <c>strict</c> | <c>balanced</c> |
    /// <c>eager</c>. Drives <see cref="Lifecycle.LifecycleOptions.HighSignalConfidence"/> and
    /// <see cref="Lifecycle.LifecycleOptions.StagedTtlDays"/> — and nothing else. See
    /// <see cref="Attentiveness"/> for why this is a string.</summary>
    public string? Certainty { get; init; }

    /// <summary>"How much detail" (v2-008 R1.3): <c>minimal</c> | <c>balanced</c> | <c>rich</c>.
    /// Drives <see cref="StatementMaxChars"/> and
    /// <see cref="Episodes.EpisodeOptions.SampleMaxChars"/> together, plus the distiller's detail
    /// directive. See <see cref="Attentiveness"/> for why this is a string.</summary>
    public string? Detail { get; init; }

    /// <summary>How often the foreground window is polled.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a window must hold focus before it is a capture candidate.</summary>
    public TimeSpan DwellThreshold { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>The minimum time before the same unchanged window is re-examined — bounds
    /// re-extraction so a window held in focus isn't re-read on every poll. Content changes
    /// within this window are caught at the next re-examination.
    ///
    /// <para>25s, not the 30s shipped before v2-008: <c>balanced</c> is deliberately not
    /// byte-identical to the old defaults. Paired with a 40-observation → 48 cap it holds episode
    /// length and AI call volume where they were while keeping the three attentiveness stops
    /// (40/25/15) in a usable band. Readings rise ~20%; that is a stated release note, not a
    /// silent change.</para></summary>
    public TimeSpan RecaptureInterval { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>The minimum time before the same window <b>handle</b> is re-extracted after a
    /// <b>title-only</b> change (v2-008 R6.2's consequence). Shorter than
    /// <see cref="RecaptureInterval"/>, because a new title is real evidence that the content
    /// moved on — but not zero, because plenty of titles churn without the content moving at
    /// all: a media player's elapsed clock, a terminal's progress line, an unread badge.
    ///
    /// <para>Before R6.2 the dwell timer was accidentally doing this job — every title change
    /// reset it, so a fast-retitling window never dwelled and was never captured. Fixing that
    /// left <c>CaptureAgent.ShouldProcess</c>, which keys on handle + title, with nothing to
    /// hold it back: at a 2s poll such a window was extracted on <em>every</em> poll, ~1,800
    /// readings an hour against ~144 for every other window, with OCR on the expensive path
    /// and nearly all of it absorbed downstream as near-duplicates. Ten seconds puts it at
    /// ~2.5× a static window rather than ~12×, while still re-reading genuinely new content
    /// well inside the time a person spends reading it.</para>
    ///
    /// <para><see cref="Attentiveness"/> scales it 15 / 10 / 6 seconds, holding that ~2.5× ratio
    /// against each stop's <see cref="RecaptureInterval"/>.</para></summary>
    public TimeSpan TitleRecaptureInterval { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The character cap the distiller is told to keep a statement under (v2-008 R1.3),
    /// moved by <see cref="Detail"/> across 120 / 200 / 500.
    ///
    /// <para>It never moves alone: <see cref="Episodes.EpisodeOptions.SampleMaxChars"/> moves with
    /// it, because the model cannot write "seat 14C" if the sample it read was truncated before
    /// that text. Raising this cap on its own produces longer statements with no more information
    /// in them.</para></summary>
    public int StatementMaxChars { get; init; } = 200;

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
    /// <para>Live since v2-008 R2: it rides in <see cref="CaptureSnapshot"/>, so the switch takes
    /// effect on the next poll rather than the next agent start. T007 had to leave it
    /// startup-bound because the live snapshot carried only pause + blocklist at the time.</para></summary>
    public bool Diagnostics { get; init; }

    /// <summary>Executables the user never wants captured.</summary>
    public IReadOnlyList<string> BlocklistApps { get; init; } = [];

    /// <summary>Keywords that drop a capture when found in a title or text.</summary>
    public IReadOnlyList<string> BlocklistKeywords { get; init; } = [];
}
