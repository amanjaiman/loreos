namespace Lore.Agent.Capture;

/// <summary>The bounded memory of recent captures the smart gate compares against. Holds
/// at most <c>MaxCaptureHistory</c> entries, newest first, dropping the oldest as new ones
/// arrive. Answers two questions: what was the last capture of a given window, and is
/// there a near-identical capture still inside the recent-duplicate window. Pure and
/// deterministic (time is passed in), so the gate is unit-testable.</summary>
public sealed class RecentCaptureGate
{
    private readonly int _maxHistory;
    private readonly LinkedList<CaptureHistoryEntry> _history = new();

    public RecentCaptureGate(int maxHistory)
    {
        if (maxHistory < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxHistory), maxHistory, "History size must be at least 1.");
        }

        _maxHistory = maxHistory;
    }

    /// <summary>The recent entries, newest first.</summary>
    public IReadOnlyCollection<CaptureHistoryEntry> Entries => _history;

    /// <summary>Record a capture, evicting the oldest entry past the cap.</summary>
    public void Record(CaptureHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _history.AddFirst(entry);
        if (_history.Count > _maxHistory)
        {
            _history.RemoveLast();
        }
    }

    /// <summary>The most recent capture of the same window (matched by handle and title),
    /// or <c>null</c> if this window hasn't been captured recently.</summary>
    public CaptureHistoryEntry? LastForWindow(long handle, string title)
    {
        foreach (CaptureHistoryEntry entry in _history)
        {
            if (entry.WindowHandle == handle && string.Equals(entry.Title, title, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>True if any capture within <paramref name="window"/> of <paramref name="now"/>
    /// is at least <paramref name="similarityThreshold"/> similar to
    /// <paramref name="text"/> — a near-duplicate worth suppressing no matter which window
    /// produced it.</summary>
    public bool HasRecentDuplicate(
        string text, DateTimeOffset now, TimeSpan window, double similarityThreshold)
    {
        foreach (CaptureHistoryEntry entry in _history)
        {
            if (now - entry.At > window)
            {
                continue;
            }

            if (TextSimilarity.Similarity(entry.Text, text) >= similarityThreshold)
            {
                return true;
            }
        }

        return false;
    }
}
