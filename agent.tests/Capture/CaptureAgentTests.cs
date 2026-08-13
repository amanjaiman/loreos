using System.Diagnostics;
using Lore.Agent.Capture;
using Lore.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace Lore.Agent.Tests.Capture;

public sealed class CaptureAgentTests
{
    private readonly ITestOutputHelper _output;

    public CaptureAgentTests(ITestOutputHelper output) => _output = output;

    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StubExtractor : ITextExtractor
    {
        private readonly ExtractedText _result;

        public StubExtractor(string text, ExtractionSource source = ExtractionSource.UiAutomation) =>
            _result = string.IsNullOrEmpty(text) ? ExtractedText.Empty : new ExtractedText(text, source);

        public Task<ExtractedText> ExtractAsync(WindowSnapshot window, CancellationToken cancellationToken = default) =>
            Task.FromResult(_result);
    }

    private sealed class AllowProbe : IWindowSecurityProbe
    {
        public bool HasProtectedContent(WindowSnapshot window) => false;
    }

    private sealed class ReadySignal : IReadinessSignal
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingEpisodeProcessor : Lore.Agent.Capture.Episodes.IEpisodeProcessor
    {
        public List<Lore.Agent.Capture.Episodes.Episode> Processed { get; } = [];

        public bool ThrowOnProcess { get; set; }

        public Task ProcessAsync(
            Lore.Agent.Capture.Episodes.Episode episode, CancellationToken cancellationToken = default)
        {
            if (ThrowOnProcess)
            {
                throw new InvalidOperationException("distiller exploded");
            }

            Processed.Add(episode);
            return Task.CompletedTask;
        }
    }

    private sealed class Harness : IDisposable
    {
        public required ActivityStore Activity { get; init; }
        public required CaptureMetrics Metrics { get; init; }
        public required CaptureAgent Agent { get; init; }
        public required FakeTimeProvider Time { get; init; }
        public required RecordingEpisodeProcessor Episodes { get; init; }

        public void Dispose() => Activity.Dispose();
    }

    private static readonly WindowSnapshot Window = new(1, "editor", "Designing Data-Intensive Apps");

    private static Harness Build(
        string extractedText = "chapter five on replication",
        IEnumerable<string>? blockedApps = null,
        CaptureOptions? options = null,
        Func<FakeTimeProvider, IForegroundWindowSource>? windowSource = null,
        ITextExtractor? extractor = null)
    {
        var time = new FakeTimeProvider();
        var activity = new ActivityStore(":memory:");
        var metrics = new CaptureMetrics();
        var monitor = new WindowMonitor(
            windowSource?.Invoke(time) ?? new FixedSource(Window), time, TimeSpan.Zero);
        CaptureOptions captureOptions = options ?? new CaptureOptions();
        var settings = new LiveCaptureSettings(new CaptureOptions
        {
            Enabled = captureOptions.Enabled,
            BlocklistApps = (blockedApps ?? []).ToArray(),
        });
        var filter = new SensitivityFilter(settings, new AllowProbe());
        var processor = new RecordingEpisodeProcessor();

        var agent = new CaptureAgent(
            captureOptions,
            settings,
            monitor,
            extractor ?? new StubExtractor(extractedText),
            filter,
            new CaptureStatusTracker(),
            activity,
            metrics,
            new ReadySignal(),
            time,
            NullLogger<CaptureAgent>.Instance,
            new Lore.Agent.Capture.Episodes.EpisodeBuilder(captureOptions.Episodes),
            processor);

        return new Harness
        {
            Activity = activity,
            Metrics = metrics,
            Agent = agent,
            Time = time,
            Episodes = processor,
        };
    }

    private sealed class FixedSource : IForegroundWindowSource
    {
        private readonly WindowSnapshot _window;

        public FixedSource(WindowSnapshot window) => _window = window;

        public WindowSnapshot Current() => _window;
    }

    /// <summary>Replays a scripted trace: each poll advances the fake clock to the entry's
    /// offset and surfaces its window; after the script there is no foreground window.</summary>
    private sealed class ScriptedSource : IForegroundWindowSource
    {
        private readonly FakeTimeProvider _time;
        private readonly Queue<(TimeSpan At, WindowSnapshot Window)> _script;

        public ScriptedSource(FakeTimeProvider time, IEnumerable<(TimeSpan At, WindowSnapshot Window)> script)
        {
            _time = time;
            _script = new(script);
        }

        public WindowSnapshot Current()
        {
            if (_script.Count == 0)
            {
                return WindowSnapshot.None;
            }

            (TimeSpan at, WindowSnapshot window) = _script.Dequeue();
            _time.Now = DateTimeOffset.UnixEpoch + at;
            return window;
        }
    }

    private sealed class TitleMappedExtractor : ITextExtractor
    {
        private readonly IReadOnlyDictionary<string, string> _textByTitle;

        public TitleMappedExtractor(IReadOnlyDictionary<string, string> textByTitle) =>
            _textByTitle = textByTitle;

        public Task<ExtractedText> ExtractAsync(WindowSnapshot window, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExtractedText(_textByTitle[window.Title], ExtractionSource.UiAutomation));
    }

    // ── Episode intake ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_observation_joins_an_episode()
    {
        using Harness h = Build();

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Observed, outcome);
        Assert.Empty(h.Episodes.Processed); // episode still open
        Assert.Equal(1, h.Metrics.Snapshot().Observed);
    }

    [Fact]
    public async Task A_continuity_break_closes_persists_and_processes_the_episode()
    {
        using Harness h = Build();

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);
        h.Time.Now += TimeSpan.FromMinutes(10); // beyond the continuity gap
        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(
            new WindowSnapshot(2, "browser", "Totally unrelated news"), CancellationToken.None);

        Assert.Equal(CaptureOutcome.EpisodeClosed, outcome);
        Lore.Agent.Capture.Episodes.Episode episode = Assert.Single(h.Episodes.Processed);
        Assert.Equal(1, episode.ObservationCount);

        IReadOnlyList<Lore.Agent.Capture.Episodes.Episode> stored =
            await h.Activity.GetRecentEpisodesAsync();
        Assert.Equal(episode.Id, Assert.Single(stored).Id);
        IReadOnlyList<DecisionEntry> decisions = await h.Activity.GetRecentDecisionsAsync();
        Assert.Equal("closed", Assert.Single(decisions).Action);
        Assert.Equal(episode.Id, decisions[0].EpisodeId);
        Assert.Equal(1, h.Metrics.Snapshot().EpisodesClosed);
    }

    [Fact]
    public async Task Blocklisted_content_is_filtered_before_episode_intake()
    {
        using Harness h = Build(blockedApps: ["editor"]);

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Filtered, outcome);
        Assert.Equal(1, h.Metrics.Snapshot().Filtered[FilterReason.BlockedApp]);
        Assert.Empty(h.Episodes.Processed);
        IReadOnlyList<Lore.Agent.Capture.Episodes.Episode> stored =
            await h.Activity.GetRecentEpisodesAsync();
        Assert.Empty(stored); // filtered text never reaches episode intake
    }

    [Fact]
    public async Task Shutdown_flushes_the_open_episode()
    {
        using Harness h = Build(options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

        await h.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => h.Metrics.Snapshot().Observed > 0, TimeSpan.FromSeconds(5));
        await h.Agent.StopAsync(CancellationToken.None);

        Assert.NotEmpty(h.Episodes.Processed); // flushed on stop, not lost
    }

    [Fact]
    public async Task Processor_failure_never_kills_the_capture_path()
    {
        using Harness h = Build();
        h.Episodes.ThrowOnProcess = true;

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);
        h.Time.Now += TimeSpan.FromMinutes(10);
        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(
            new WindowSnapshot(2, "browser", "Unrelated"), CancellationToken.None);

        Assert.Equal(CaptureOutcome.EpisodeClosed, outcome); // failure logged, not thrown
        Assert.Single(await h.Activity.GetRecentEpisodesAsync()); // episode persisted first
    }

    [Fact]
    public async Task A_disabled_loop_captures_nothing()
    {
        using Harness h = Build(options: new CaptureOptions { Enabled = false });

        await h.Agent.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await h.Agent.StopAsync(CancellationToken.None);

        Assert.Equal(0, h.Metrics.Snapshot().Observed);
        Assert.Empty(h.Episodes.Processed);
    }

    [Fact]
    public void Constructor_validates_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureAgent(
            null!,
            new LiveCaptureSettings(new CaptureOptions()),
            new WindowMonitor(new FixedSource(Window), new FakeTimeProvider(), TimeSpan.Zero),
            new StubExtractor("x"), new SensitivityFilter(
                new LiveCaptureSettings(new CaptureOptions()), new AllowProbe()),
            new CaptureStatusTracker(),
            new ActivityStore(":memory:"), new CaptureMetrics(), new ReadySignal(),
            new FakeTimeProvider(), NullLogger<CaptureAgent>.Instance,
            new Lore.Agent.Capture.Episodes.EpisodeBuilder(new Lore.Agent.Capture.Episodes.EpisodeOptions()),
            new RecordingEpisodeProcessor()));
    }

    // End-to-end trace through the hosted loop: a stretch of related reading, a blocked
    // password vault, a switch to coding with a near-duplicate page, then shutdown. The
    // persisted episodes + decisions tables are the observable outcome (v2-001 T004).
    [Fact]
    public async Task A_day_trace_segments_into_persisted_episodes_with_a_decision_trail()
    {
        var texts = new Dictionary<string, string>
        {
            ["Wisdom tooth recovery — NHS"] = "aftercare instructions for wisdom tooth extraction bleeding and swelling basics",
            ["Soft foods after tooth extraction"] = "soft foods list yogurt soup mashed potatoes for the first week of recovery",
            ["KeePass — personal vault"] = "master password vault entries banking credentials",
            ["Wisdom tooth recovery timeline"] = "recovery timeline day three swelling peaks then subsides with aftercare",
            ["recall.rs — lore"] = "fn blend recency similarity weighted scores for memory recall ranking",
            ["budget.rs — lore"] = "token budget arithmetic for prompt assembly in the distiller",
            ["budget.rs — lore (2)"] = "token budget arithmetic for prompt assembly in the distiller",
        };
        (TimeSpan, WindowSnapshot)[] script =
        [
            (TimeSpan.Zero, new WindowSnapshot(1, "browser", "Wisdom tooth recovery — NHS")),
            (TimeSpan.FromSeconds(45), new WindowSnapshot(2, "browser", "Soft foods after tooth extraction")),
            (TimeSpan.FromSeconds(90), new WindowSnapshot(3, "keepass", "KeePass — personal vault")),
            (TimeSpan.FromSeconds(135), new WindowSnapshot(4, "browser", "Wisdom tooth recovery timeline")),
            (TimeSpan.FromSeconds(180), new WindowSnapshot(5, "code", "recall.rs — lore")),
            (TimeSpan.FromSeconds(225), new WindowSnapshot(6, "code", "budget.rs — lore")),
            (TimeSpan.FromSeconds(270), new WindowSnapshot(7, "code", "budget.rs — lore (2)")),
        ];
        using Harness h = Build(
            blockedApps: ["keepass"],
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(time, script),
            extractor: new TitleMappedExtractor(texts));

        await h.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => h.Metrics.Snapshot().Observed >= 6, TimeSpan.FromSeconds(10));
        await h.Agent.StopAsync(CancellationToken.None);

        IReadOnlyList<Lore.Agent.Capture.Episodes.Episode> episodes =
            await h.Activity.GetRecentEpisodesAsync();
        Assert.Equal(2, episodes.Count); // newest first
        Lore.Agent.Capture.Episodes.Episode coding = episodes[0];
        Lore.Agent.Capture.Episodes.Episode reading = episodes[1];

        Assert.Equal("browser", Assert.Single(reading.Executables));
        Assert.Equal(3, reading.ObservationCount); // the vault window is not among them
        Assert.Equal(3, reading.Samples.Count);
        Assert.Equal(TimeSpan.FromSeconds(135), reading.EndedAt - reading.StartedAt);

        Assert.Equal("code", Assert.Single(coding.Executables));
        Assert.Equal(3, coding.ObservationCount);
        Assert.Equal(2, coding.Samples.Count); // identical budget.rs text absorbed as a near-duplicate

        Assert.All(episodes, e => Assert.DoesNotContain(
            e.Titles.Concat(e.Samples).Concat(e.Executables),
            s => s.Contains("vault", StringComparison.OrdinalIgnoreCase)
                || s.Contains("keepass", StringComparison.OrdinalIgnoreCase)));

        IReadOnlyList<DecisionEntry> decisions = await h.Activity.GetRecentDecisionsAsync();
        Assert.Equal(2, decisions.Count); // newest first
        Assert.All(decisions, d => Assert.Equal("closed", d.Action));
        Assert.Equal("shutdown_flush", decisions[0].Reason);
        Assert.Equal(coding.Id, decisions[0].EpisodeId);
        Assert.Equal("continuity_break_or_bound", decisions[1].Reason);
        Assert.Equal(reading.Id, decisions[1].EpisodeId);

        ActivityLogEntry filteredRow = Assert.Single(
            await h.Activity.GetRecentActivityAsync(), a => a.Decision == ActivityDecision.Filtered);
        Assert.Equal("keepass", filteredRow.Executable);

        Assert.Equal(2, h.Episodes.Processed.Count); // both handed through the processor seam

        _output.WriteLine("episodes table (newest first):");
        foreach (Lore.Agent.Capture.Episodes.Episode e in episodes)
        {
            _output.WriteLine(
                $"  {e.Id}  {e.StartedAt:HH:mm:ss}-{e.EndedAt:HH:mm:ss}  " +
                $"apps=[{string.Join(", ", e.Executables)}] observations={e.ObservationCount}");
            foreach (string title in e.Titles)
            {
                _output.WriteLine($"    title:  {title}");
            }

            foreach (string sample in e.Samples)
            {
                _output.WriteLine($"    sample: {sample}");
            }
        }

        _output.WriteLine("decisions table (newest first):");
        foreach (DecisionEntry d in decisions)
        {
            _output.WriteLine($"  {d.At:HH:mm:ss}  episode={d.EpisodeId} action={d.Action} reason={d.Reason}");
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }
}
