using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class WindowMonitorTests
{
    private static readonly TimeSpan Dwell = TimeSpan.FromSeconds(3);

    private sealed class FakeWindowSource : IForegroundWindowSource
    {
        public WindowSnapshot Next { get; set; } = WindowSnapshot.None;

        public WindowSnapshot Current() => Next;
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    private static (WindowMonitor Monitor, FakeWindowSource Source, FakeTimeProvider Time) Build()
    {
        var source = new FakeWindowSource();
        var time = new FakeTimeProvider();
        return (new WindowMonitor(source, time, Dwell), source, time);
    }

    private static WindowSnapshot Window(long handle, string title = "doc") =>
        new(handle, "app.exe", title);

    [Fact]
    public void First_sight_of_a_window_is_a_window_change_and_has_not_dwelled()
    {
        (WindowMonitor monitor, FakeWindowSource source, _) = Build();
        source.Next = Window(1);

        WindowObservation observation = monitor.Poll();

        Assert.Equal(WindowChange.WindowChanged, observation.Change);
        Assert.False(observation.HasDwelled);
        Assert.Equal(TimeSpan.Zero, observation.Dwell);
        Assert.Equal(Window(1), observation.Window);
    }

    [Fact]
    public void Window_does_not_dwell_before_the_threshold()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1);
        monitor.Poll();

        time.Advance(Dwell - TimeSpan.FromMilliseconds(1));
        WindowObservation observation = monitor.Poll();

        Assert.Equal(WindowChange.Unchanged, observation.Change);
        Assert.False(observation.HasDwelled);
    }

    [Fact]
    public void Window_dwells_once_focus_passes_the_threshold()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1);
        monitor.Poll();

        time.Advance(Dwell);
        WindowObservation observation = monitor.Poll();

        Assert.Equal(WindowChange.Unchanged, observation.Change);
        Assert.True(observation.HasDwelled);
        Assert.Equal(Dwell, observation.Dwell);
    }

    [Fact]
    public void Dwell_is_level_triggered_and_stays_true_while_focus_holds()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1);
        monitor.Poll();

        time.Advance(Dwell);
        Assert.True(monitor.Poll().HasDwelled);

        time.Advance(TimeSpan.FromSeconds(10));
        Assert.True(monitor.Poll().HasDwelled);
    }

    [Fact]
    public void Switching_window_resets_the_dwell_timer()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1);
        monitor.Poll();
        time.Advance(Dwell);
        Assert.True(monitor.Poll().HasDwelled);

        source.Next = Window(2);
        WindowObservation switched = monitor.Poll();

        Assert.Equal(WindowChange.WindowChanged, switched.Change);
        Assert.False(switched.HasDwelled);
        Assert.Equal(TimeSpan.Zero, switched.Dwell);
    }

    // v2-008 R6.2. This test asserted the opposite until then — a title change reset dwell.
    [Fact]
    public void Changing_title_on_the_same_window_continues_the_existing_dwell()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1, "first");
        monitor.Poll();
        time.Advance(Dwell);
        Assert.True(monitor.Poll().HasDwelled);

        source.Next = Window(1, "second");
        WindowObservation retitled = monitor.Poll();

        // The user did not look away; the app relabelled itself. Dwell already earned stands,
        // and the reported change is still TitleChanged so the loop's handle+title key sees a
        // new key and re-captures the new content.
        Assert.Equal(WindowChange.TitleChanged, retitled.Change);
        Assert.True(retitled.HasDwelled);
        Assert.Equal(Dwell, retitled.Dwell);
    }

    [Fact]
    public void A_window_that_retitles_every_poll_still_dwells_and_is_capturable()
    {
        // The defect this replaces: a media player counting elapsed time, a terminal printing
        // progress, or a chat app with an unread badge rewrites its title faster than the
        // dwell threshold. Resetting on every title change meant dwell never accumulated and
        // the window could NEVER be captured, no matter how long it was held.
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        TimeSpan poll = TimeSpan.FromSeconds(1); // faster than the 3s dwell threshold

        var seen = new List<WindowObservation>();
        for (int tick = 0; tick < 10; tick++)
        {
            source.Next = Window(1, $"Now playing — 0:{tick:00}");
            seen.Add(monitor.Poll());
            time.Advance(poll);
        }

        // Only the very first sighting is a window change; every later poll is title-only.
        Assert.Equal(WindowChange.WindowChanged, seen[0].Change);
        Assert.All(seen.Skip(1), o => Assert.Equal(WindowChange.TitleChanged, o.Change));
        Assert.True(seen[^1].HasDwelled);
        Assert.Equal(TimeSpan.FromSeconds(9), seen[^1].Dwell);

        // Precisely: dwelled from the first poll that crossed the threshold onward.
        Assert.Equal(3, seen.Count(o => !o.HasDwelled));
    }

    [Fact]
    public void No_foreground_window_reports_none_and_never_dwells()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = WindowSnapshot.None;

        WindowObservation observation = monitor.Poll();

        Assert.Equal(WindowChange.None, observation.Change);
        Assert.False(observation.HasDwelled);
        Assert.Equal(TimeSpan.Zero, observation.Dwell);

        // Even after a long idle stretch, an empty desktop is never a capture candidate.
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.False(monitor.Poll().HasDwelled);
    }

    [Fact]
    public void Returning_to_a_window_after_idle_starts_a_fresh_dwell()
    {
        (WindowMonitor monitor, FakeWindowSource source, FakeTimeProvider time) = Build();
        source.Next = Window(1);
        monitor.Poll();

        source.Next = WindowSnapshot.None;
        monitor.Poll();

        source.Next = Window(1);
        WindowObservation reappeared = monitor.Poll();

        Assert.Equal(WindowChange.WindowChanged, reappeared.Change);
        Assert.False(reappeared.HasDwelled);
    }

    [Fact]
    public void Constructor_rejects_a_negative_dwell_threshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WindowMonitor(new FakeWindowSource(), new FakeTimeProvider(), TimeSpan.FromSeconds(-1)));
    }
}
