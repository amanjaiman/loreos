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
/// <para>Switching <em>window</em> resets the dwell timer — a new thing to look at has to
/// earn its own dwell. A title change within the same window does NOT: it continues the
/// dwell already accumulated. That is deliberate (v2-008 R6.2). Plenty of apps rewrite
/// their own title faster than the dwell threshold — media players counting elapsed time,
/// terminals printing progress, chat apps with an unread badge in the title — and resetting
/// on every title change meant those windows could never accumulate dwell and were
/// therefore NEVER captured, silently, no matter how long the user sat in front of them.
/// The user did not switch away; the app just relabelled itself, so the dwell it has
/// already earned still stands.</para>
///
/// <para>New content is still read promptly: <c>CaptureAgent.ShouldProcess</c> keys on
/// handle + title, so a retitled window is a new key and is re-captured on the next tick.
/// Dwell answers "has the user settled here?", which the title does not speak to; the key
/// answers "is this the same content?", which it does.</para></summary>
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
    /// poll. Resets the dwell timer on a window change only; a title change within the same
    /// window carries the accumulated dwell forward (v2-008 R6.2).</summary>
    public WindowObservation Poll()
    {
        WindowSnapshot next = _source.Current();
        DateTimeOffset now = _time.GetUtcNow();
        WindowChange change = Classify(_current, next);

        // The snapshot advances on ANY change, including a title-only one — otherwise every
        // later poll would keep comparing against the stale title and re-report TitleChanged
        // forever. Only the dwell clock treats the two kinds of change differently.
        if (change != WindowChange.Unchanged)
        {
            _current = next;
        }

        // Returning from an empty desktop classifies as WindowChanged (previous.IsEmpty), so
        // a stretch with no foreground window still costs the window its dwell — it is
        // genuinely a fresh arrival — without needing a reset on the None transition itself.
        if (change == WindowChange.WindowChanged)
        {
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
