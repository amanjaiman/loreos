using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class RecentCaptureGateTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static CaptureHistoryEntry Entry(
        long handle, string title, string text, DateTimeOffset at, ContentType type = ContentType.Unknown) =>
        new(handle, title, text, type, at);

    [Fact]
    public void History_is_capped_and_evicts_the_oldest()
    {
        var gate = new RecentCaptureGate(maxHistory: 2);
        gate.Record(Entry(1, "a", "first", T0));
        gate.Record(Entry(2, "b", "second", T0));
        gate.Record(Entry(3, "c", "third", T0));

        Assert.Equal(2, gate.Entries.Count);
        Assert.Null(gate.LastForWindow(1, "a"));        // oldest evicted
        Assert.NotNull(gate.LastForWindow(3, "c"));     // newest kept
    }

    [Fact]
    public void Entries_are_newest_first()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "a", "first", T0));
        gate.Record(Entry(2, "b", "second", T0));

        Assert.Equal("second", gate.Entries.First().Text);
    }

    [Fact]
    public void LastForWindow_matches_handle_and_title_and_returns_the_most_recent()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "doc", "old", T0));
        gate.Record(Entry(1, "doc", "new", T0.AddSeconds(1)));

        CaptureHistoryEntry? last = gate.LastForWindow(1, "doc");

        Assert.NotNull(last);
        Assert.Equal("new", last!.Text);
    }

    [Fact]
    public void LastForWindow_distinguishes_the_same_handle_with_a_different_title()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "tab one", "one", T0));

        Assert.NotNull(gate.LastForWindow(1, "tab one"));
        Assert.Null(gate.LastForWindow(1, "tab two"));
    }

    [Fact]
    public void HasRecentDuplicate_finds_a_similar_entry_inside_the_window()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "a", "the quick brown fox", T0));

        bool duplicate = gate.HasRecentDuplicate(
            "the quick brown fox", T0.AddSeconds(10), TimeSpan.FromSeconds(30), similarityThreshold: 0.9);

        Assert.True(duplicate);
    }

    [Fact]
    public void HasRecentDuplicate_ignores_entries_outside_the_window()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "a", "the quick brown fox", T0));

        bool duplicate = gate.HasRecentDuplicate(
            "the quick brown fox", T0.AddSeconds(60), TimeSpan.FromSeconds(30), similarityThreshold: 0.9);

        Assert.False(duplicate); // too old
    }

    [Fact]
    public void HasRecentDuplicate_ignores_entries_below_the_threshold()
    {
        var gate = new RecentCaptureGate(maxHistory: 5);
        gate.Record(Entry(1, "a", "alpha beta gamma", T0));

        bool duplicate = gate.HasRecentDuplicate(
            "delta epsilon zeta", T0.AddSeconds(5), TimeSpan.FromSeconds(30), similarityThreshold: 0.5);

        Assert.False(duplicate);
    }

    [Fact]
    public void Constructor_rejects_a_history_smaller_than_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecentCaptureGate(maxHistory: 0));
    }

    [Fact]
    public void Record_rejects_a_null_entry()
    {
        var gate = new RecentCaptureGate(maxHistory: 1);
        Assert.Throws<ArgumentNullException>(() => gate.Record(null!));
    }
}
