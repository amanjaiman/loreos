namespace Lore.Agent.Capture;

/// <summary>The live "what is Lore watching right now" signal for the app rail (spec 005 R2). The
/// capture loop is the sole writer: on every processed window it records the title only when the
/// window cleared the whole filter chain, and clears it the instant a window is excluded — so a
/// reader can never be handed a title the filter would have blocked (binding rule 1). The snapshot
/// is a single immutable reference published atomically, so the API thread reads a consistent pair
/// without taking a lock.</summary>
public sealed class CaptureStatusTracker
{
    private Snapshot _current = Snapshot.None;

    /// <summary>The current capture target. <see cref="CaptureTarget.WindowTitle"/> is null when
    /// the current window was excluded, capture has not run, or nothing has been observed yet — an
    /// excluded window is deliberately indistinguishable from an idle one, so a reader can never
    /// infer blocked activity from the field.</summary>
    public CaptureTarget Current
    {
        get
        {
            Snapshot snapshot = Volatile.Read(ref _current);
            return new CaptureTarget(snapshot.WindowTitle, snapshot.ObservedAt);
        }
    }

    /// <summary>Record that the current window cleared the filter chain. Its title survived the
    /// same title screening capture applies (blocklist keyword + sensitive-pattern), so it is safe
    /// to surface verbatim as the redacted title.</summary>
    public void RecordCaptured(string windowTitle, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(windowTitle);
        Volatile.Write(ref _current, new Snapshot(windowTitle, observedAt));
    }

    /// <summary>Record that the current window was excluded by the filter chain. The title and its
    /// timestamp are dropped so the excluded window leaves no trace a reader could use.</summary>
    public void RecordExcluded() => Volatile.Write(ref _current, Snapshot.None);

    private sealed record Snapshot(string? WindowTitle, DateTimeOffset? ObservedAt)
    {
        public static readonly Snapshot None = new(null, null);
    }
}

/// <summary>The current capture target: the redacted title of the window Lore is watching and when
/// it last observed it, or nulls when there is nothing to show.</summary>
public readonly record struct CaptureTarget(string? WindowTitle, DateTimeOffset? ObservedAt);
