namespace Lore.Agent.Capture;

/// <summary>The front of the capture loop: polls the foreground window through the
/// Win32 seam, tracks how long the current window has held focus, and reports a window
/// as a capture candidate only once it has dwelled past the threshold (spec 003 T001).
///
/// <para>Dwell is <em>level-triggered</em>: <see cref="WindowObservation.HasDwelled"/>
/// stays true on every poll while the window keeps focus past the threshold. The loop
/// (T008) still asks the smart gate (T005) whether to actually capture, so a window
/// that dwells is offered repeatedly rather than firing once and being lost if a gate
/// skips it.</para>
///
/// <para>Switching window or changing title resets the dwell timer — a new thing to
/// look at has to earn its own dwell.</para></summary>
public sealed class WindowMonitor
{
    private readonly IForegroundWindowSource _source;
    private readonly TimeProvider _time;
    private readonly TimeSpan _dwellThreshold;

    private WindowSnapshot _current = WindowSnapshot.None;
    private DateTimeOffset _focusedSince;

    public WindowMonitor(IForegroundWindowSource source, TimeProvider time, TimeSpan dwellThreshold)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(time);
        if (dwellThreshold < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dwellThreshold), dwellThreshold, "Dwell threshold cannot be negative.");
        }

        _source = source;
        _time = time;
        _dwellThreshold = dwellThreshold;
    }

    /// <summary>Read the foreground window once and classify it against the previous
    /// poll. Resets the dwell timer on any window or title change.</summary>
    public WindowObservation Poll()
    {
        WindowSnapshot next = _source.Current();
        DateTimeOffset now = _time.GetUtcNow();
        WindowChange change = Classify(_current, next);

        if (change != WindowChange.Unchanged)
        {
            _current = next;
            _focusedSince = now;
        }

        if (next.IsEmpty)
        {
            return new WindowObservation(next, change, TimeSpan.Zero, HasDwelled: false);
        }

        TimeSpan dwell = now - _focusedSince;
        return new WindowObservation(next, change, dwell, dwell >= _dwellThreshold);
    }

    private static WindowChange Classify(WindowSnapshot previous, WindowSnapshot next)
    {
        if (next.IsEmpty)
        {
            return WindowChange.None;
        }

        if (previous.IsEmpty || previous.Handle != next.Handle)
        {
            return WindowChange.WindowChanged;
        }

        return string.Equals(previous.Title, next.Title, StringComparison.Ordinal)
            ? WindowChange.Unchanged
            : WindowChange.TitleChanged;
    }
}
