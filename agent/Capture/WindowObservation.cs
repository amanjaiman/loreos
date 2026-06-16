namespace Lore.Agent.Capture;

/// <summary>What changed about the foreground window between two consecutive polls.
/// Only window and title transitions are tracked here — they are what the dwell timer
/// keys off. Content-level change detection needs extracted text and so lives in the
/// similarity gates (spec 003 T004/T005), not the monitor.</summary>
public enum WindowChange
{
    /// <summary>Same window and same title as the previous poll.</summary>
    Unchanged,

    /// <summary>A different top-level window is in front (the handle changed).</summary>
    WindowChanged,

    /// <summary>The same window, but its title changed — a new document, tab, or page.</summary>
    TitleChanged,

    /// <summary>There is no foreground window to observe (locked or idle).</summary>
    None,
}

/// <summary>One poll's worth of monitor output: the window seen, how it changed since
/// the last poll, how long it has held focus, and whether that dwell has passed the
/// threshold that makes it a capture candidate.</summary>
public sealed record WindowObservation(
    WindowSnapshot Window,
    WindowChange Change,
    TimeSpan Dwell,
    bool HasDwelled);
