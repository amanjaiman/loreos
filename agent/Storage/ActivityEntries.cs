namespace Lore.Agent.Storage;

/// <summary>What the capture loop decided about a window — the vocabulary of the
/// human-readable activity log.</summary>
public enum ActivityDecision
{
    /// <summary>A memory was stored.</summary>
    Captured,

    /// <summary>The smart gate skipped it (unchanged, duplicate, heartbeat, …).</summary>
    Skipped,

    /// <summary>The sensitivity filter dropped it (blocklist, password, pattern).</summary>
    Filtered,
}

/// <summary>One human-readable row of the activity log: what Lore did with a window and
/// why. Operational telemetry the user can inspect — it never leaves the machine.</summary>
public sealed record ActivityLogEntry(
    DateTimeOffset At,
    string Executable,
    string WindowTitle,
    ActivityDecision Decision,
    string Reason,
    string Observation,
    string Category);

/// <summary>One raw-capture telemetry row: the filtered text that was captured plus how it
/// was read. Lets the user audit exactly what Lore saw. Local only — never sent to mem0 or
/// a model.</summary>
public sealed record RawCaptureEntry(
    DateTimeOffset At,
    string Executable,
    string WindowTitle,
    string ExtractionSource,
    string ContentType,
    string Text);

/// <summary>One row of the v2-001 decision trail: what the pipeline did with an episode
/// or a candidate fact, and why. This table IS the spec's explainability guarantee —
/// "why does/doesn't Lore know X?" is answered here. <see cref="Action"/> vocabulary is
/// owned by the pipeline (closed · distill_failed · stored · staged · reinforced ·
/// promoted · arbitrated · deferred · needs_confirmation · …); <see cref="Statement"/>,
/// <see cref="Kind"/>, and <see cref="MemoryId"/> are empty when the action concerns the
/// episode itself rather than a candidate fact.</summary>
public sealed record DecisionEntry(
    DateTimeOffset At,
    string EpisodeId,
    string Action,
    string Reason,
    string Statement,
    string Kind,
    string MemoryId);
