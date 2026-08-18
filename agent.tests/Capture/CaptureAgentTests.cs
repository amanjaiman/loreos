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

    private sealed class GatedExtractor : ITextExtractor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ExtractedText> ExtractAsync(
            WindowSnapshot window, CancellationToken cancellationToken = default)
        {
            Entered.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new ExtractedText("private draft", ExtractionSource.UiAutomation);
        }
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
        public required LiveCaptureSettings Settings { get; init; }
        public required CaptureStatusTracker Status { get; init; }

        public void Dispose() => Activity.Dispose();
    }

    private static readonly WindowSnapshot Window = new(1, "editor", "Designing Data-Intensive Apps");

    private static Harness Build(
        string extractedText = "chapter five on replication",
        IEnumerable<string>? blockedApps = null,
        CaptureOptions? options = null,
        Func<FakeTimeProvider, IForegroundWindowSource>? windowSource = null,
        ITextExtractor? extractor = null,
        TimeSpan? dwell = null)
    {
        var time = new FakeTimeProvider();
        var activity = new ActivityStore(":memory:");
        var metrics = new CaptureMetrics();

        // One LiveCaptureSettings behind the whole harness (v2-008 R2): the monitor, the loop, the
        // filter and the episode builder all read the same snapshot, so a test can move a value
        // mid-run and see every part of the pipeline follow.
        CaptureOptions captureOptions = options ?? new CaptureOptions();
        var settings = new LiveCaptureSettings(captureOptions with
        {
            DwellThreshold = dwell ?? TimeSpan.Zero,
            BlocklistApps = (blockedApps ?? []).ToArray(),
        });
        var monitor = new WindowMonitor(
            windowSource?.Invoke(time) ?? new FixedSource(Window), time, settings);
        var filter = new SensitivityFilter(settings, new AllowProbe());
        var processor = new RecordingEpisodeProcessor();

        var status = new CaptureStatusTracker();
        var agent = new CaptureAgent(
            settings,
            monitor,
            extractor ?? new StubExtractor(extractedText),
            filter,
            status,
            activity,
            metrics,
            new ReadySignal(),
            time,
            NullLogger<CaptureAgent>.Instance,
            new Lore.Agent.Capture.Episodes.EpisodeBuilder(settings),
            processor);

        return new Harness
        {
            Activity = activity,
            Metrics = metrics,
            Agent = agent,
            Time = time,
            Episodes = processor,
            Settings = settings,
            Status = status,
        };
    }

    private sealed class FixedSource : IForegroundWindowSource
    {
        private readonly WindowSnapshot _window;

        public FixedSource(WindowSnapshot window) => _window = window;

        public WindowSnapshot Current() => _window;
    }

    /// <summary>Replays a scripted trace: each poll advances the fake clock to the entry's
    /// offset and surfaces its window; after the script there is no foreground window.
    ///
    /// <para><paramref name="onPoll"/> runs on the capture loop's own thread at the given poll
    /// index, which is what lets a test change settings mid-run with no race at all.</para></summary>
    private sealed class ScriptedSource : IForegroundWindowSource
    {
        private readonly FakeTimeProvider _time;
        private readonly Queue<(TimeSpan At, WindowSnapshot Window)> _script;
        private readonly int _at;
        private readonly Action? _onPoll;

        private int _polls;

        public ScriptedSource(
            FakeTimeProvider time,
            IEnumerable<(TimeSpan At, WindowSnapshot Window)> script,
            Action? onPoll = null,
            int at = 0)
        {
            _time = time;
            _script = new(script);
            _onPoll = onPoll;
            _at = at;
        }

        public WindowSnapshot Current()
        {
            if (_polls++ == _at)
            {
                _onPoll?.Invoke();
            }

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
    public async Task Pausing_during_extraction_discards_the_in_flight_result()
    {
        var extractor = new GatedExtractor();
        using Harness h = Build(extractor: extractor);
        Task<CaptureOutcome> capture = h.Agent.CaptureOnceAsync(Window, CancellationToken.None);
        await extractor.Entered.Task;

        h.Settings.Update(new System.Text.Json.Nodes.JsonObject { ["enabled"] = false });
        extractor.Release.SetResult();

        Assert.Equal(CaptureOutcome.Filtered, await capture);
        Assert.Equal(0, h.Metrics.Snapshot().Observed);
        Assert.Null(h.Status.Current.WindowTitle);
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

    // ── Opt-in raw-capture diagnostic (v2-008 R4.2) ────────────────────────────────

    [Fact]
    public async Task Diagnostics_off_writes_no_raw_captures()
    {
        using Harness h = Build(); // the default: capture.diagnostics is off

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(1, h.Metrics.Snapshot().Observed); // the observation happened…
        Assert.Empty(await h.Activity.GetRecentRawCapturesAsync()); // …and left no raw row
    }

    [Fact]
    public async Task Diagnostics_on_records_the_post_filter_text_it_read()
    {
        using Harness h = Build(options: new CaptureOptions { Diagnostics = true });

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        RawCaptureEntry row = Assert.Single(await h.Activity.GetRecentRawCapturesAsync());
        Assert.Equal("chapter five on replication", row.Text);
        Assert.Equal("editor", row.Executable);
        Assert.Equal("UiAutomation", row.ExtractionSource);
    }

    [Fact]
    public async Task Diagnostics_never_records_text_the_filter_blocked()
    {
        // The privacy rule that makes this switch safe to offer at all: the write sits below the
        // filter chain, so turning diagnostics on cannot record one character the chain dropped.
        using Harness h = Build(
            blockedApps: ["editor"], options: new CaptureOptions { Diagnostics = true });

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Filtered, outcome);
        Assert.Empty(await h.Activity.GetRecentRawCapturesAsync());
    }

    // ── v2-008 R6.2: title churn no longer blocks capture ──────────────────────────

    /// <summary>Forty-six polls, one a second, of a single window that relabels itself on every
    /// one of them — a media player counting elapsed time, a terminal printing progress, a chat
    /// app with an unread badge — under a four-second dwell threshold.</summary>
    private static (TimeSpan, WindowSnapshot)[] RetitlingScript(bool retitle = true) =>
    [
        .. Enumerable.Range(0, 46).Select(second =>
            (TimeSpan.FromSeconds(second),
             new WindowSnapshot(
                 9, "player", retitle ? $"Ambient set — {second / 60}:{second % 60:00}" : "Ambient set"))),
    ];

    [Fact]
    public async Task A_window_that_rewrites_its_title_every_poll_is_still_captured()
    {
        // Before R6.2 each title change reset the dwell timer, so this window could never reach
        // the threshold and was NEVER captured — silently, with no activity row saying so. This
        // test is the whole defect end to end.
        //
        // The script runs 45 seconds rather than T002's 20, because T003 gave a title-only
        // re-read its own ten-second gap: "five observations inside twenty seconds" was an
        // assertion about the UNBOUNDED behaviour T002 shipped, where every retitle was a fresh
        // handle+title key and so was read on every single poll. Being captured is what R6.2
        // asks for and is what is asserted here; being captured on every poll never was.
        using Harness h = Build(
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(time, RetitlingScript()),
            dwell: TimeSpan.FromSeconds(4));

        await h.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => h.Metrics.Snapshot().Observed >= 5, TimeSpan.FromSeconds(10));
        await h.Agent.StopAsync(CancellationToken.None);

        // First captured at four seconds — the point where four seconds of dwell had accumulated
        // across the retitles rather than being reset by each one — then every ten seconds after.
        Lore.Agent.Capture.Episodes.Episode episode = Assert.Single(h.Episodes.Processed);
        Assert.Equal(5, episode.ObservationCount);
        Assert.Equal("player", Assert.Single(episode.Executables));

        // Every reading caught the new title, so the churn is genuinely being followed rather
        // than one title being sampled five times.
        Assert.Equal(5, episode.Titles.Count);
    }

    // ── v2-008 R6.2's consequence (T003): the title-only re-read gap ────────────────

    [Fact]
    public async Task A_retitling_window_is_read_on_its_own_gap_not_on_every_poll()
    {
        // The cost R6.2 left behind: ShouldProcess keys on the window, and every retitle was a
        // new key, so a window like this went from never captured to captured on EVERY poll —
        // ~1,800 OCR-bearing readings an hour against ~144 for a window that sits still. The
        // title-only gap puts it between the two: more often than an unchanged window, because a
        // new title really is evidence of new content, but nowhere near every poll.
        using Harness churning = Build(
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(time, RetitlingScript()),
            dwell: TimeSpan.FromSeconds(4));
        using Harness still = Build(
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(time, RetitlingScript(retitle: false)),
            dwell: TimeSpan.FromSeconds(4));

        await churning.Agent.StartAsync(CancellationToken.None);
        await still.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(
            () => churning.Metrics.Snapshot().Observed >= 5 && still.Metrics.Snapshot().Observed >= 2,
            TimeSpan.FromSeconds(10));
        await churning.Agent.StopAsync(CancellationToken.None);
        await still.Agent.StopAsync(CancellationToken.None);

        // 45 simulated seconds, 46 chances to read the window:
        //   unthrottled (what T002 shipped) → 42 readings
        //   title-only gap of 10s           →  5 readings, at t = 4, 14, 24, 34, 44
        //   unchanged window, 30s interval  →  2 readings, at t = 4 and 34
        Assert.Equal(5, churning.Metrics.Snapshot().Observed);
        Assert.Equal(2, still.Metrics.Snapshot().Observed);
    }

    // ── v2-008 R2: the loop's timings are live ─────────────────────────────────────

    [Fact]
    public async Task A_recapture_interval_change_speeds_the_loop_up_without_a_restart()
    {
        // R2's acceptance, inverted so it is deterministic: one window sitting still for 45
        // seconds, and a PATCH five polls in. The update is applied from inside the window
        // source, on the loop's own thread, so there is no timing race — the loop simply reads a
        // different snapshot on its next tick, mid-episode, with no restart.
        Harness? h = null;
        h = Build(
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(
                time,
                RetitlingScript(retitle: false),
                onPoll: () => h!.Settings.Update(new System.Text.Json.Nodes.JsonObject
                {
                    ["recaptureInterval"] = "00:00:05",
                }),
                at: 5),
            dwell: TimeSpan.FromSeconds(4));

        using (h)
        {
            await h.Agent.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => h.Metrics.Snapshot().Observed >= 9, TimeSpan.FromSeconds(10));
            await h.Agent.StopAsync(CancellationToken.None);

            // At the 30s default this window is read twice in 45 seconds (t = 4 and 34 — see
            // A_retitling_window_is_read_on_its_own_gap_not_on_every_poll). At 5s it is read at
            // t = 4, then every five seconds from 9 to 44.
            Assert.Equal(9, h.Metrics.Snapshot().Observed);

            // The change did not cost the open episode: all nine observations are in one.
            Lore.Agent.Capture.Episodes.Episode episode = Assert.Single(h.Episodes.Processed);
            Assert.Equal(9, episode.ObservationCount);
        }
    }

    [Fact]
    public async Task Config_garbage_never_takes_the_capture_loop_down()
    {
        // A hand-edited config.json with every bound wrong: a poll interval the binder reads as
        // two DAYS, a negative dwell, an episode cap of one, a negative sample truncation. Before
        // v2-008 the last two threw out of EpisodeBuilder's constructor at DI resolution and the
        // agent did not start at all; a negative dwell threw out of WindowMonitor's. Now they are
        // repaired at the snapshot boundary and the loop runs normally.
        using Harness h = Build(options: new CaptureOptions
        {
            PollInterval = TimeSpan.FromDays(2),
            DwellThreshold = TimeSpan.FromSeconds(-30),
            RecaptureInterval = TimeSpan.FromSeconds(-1),
            TitleRecaptureInterval = TimeSpan.FromSeconds(-1),
            Episodes = new Lore.Agent.Capture.Episodes.EpisodeOptions
            {
                MaxObservations = 1,
                MaxAge = TimeSpan.Zero,
                MaxSamples = 0,
                SampleMaxChars = -5,
            },
        });

        // MaxObservations 1 would have closed the episode on its first observation.
        Assert.Equal(
            CaptureOutcome.Observed, await h.Agent.CaptureOnceAsync(Window, CancellationToken.None));

        h.Time.Now += TimeSpan.FromMinutes(10); // continuity break closes it
        Assert.Equal(
            CaptureOutcome.EpisodeClosed,
            await h.Agent.CaptureOnceAsync(
                new WindowSnapshot(2, "browser", "Totally unrelated news"), CancellationToken.None));

        // A negative SampleMaxChars would have thrown out of the truncating range on close,
        // losing the episode on every single close for as long as the file stayed that way.
        Lore.Agent.Capture.Episodes.Episode episode = Assert.Single(h.Episodes.Processed);
        Assert.Equal("chapter five on replication", Assert.Single(episode.Samples));

        // And the same garbage arriving live, mid-run, is repaired the same way.
        h.Settings.Update(new System.Text.Json.Nodes.JsonObject
        {
            ["pollInterval"] = "not a timespan",
            ["episodes"] = new System.Text.Json.Nodes.JsonObject { ["sampleMaxChars"] = -5 },
        });
        Assert.Equal(
            new Lore.Agent.Capture.Episodes.EpisodeOptions().SampleMaxChars,
            h.Settings.Current.Episodes.SampleMaxChars);
    }

    [Fact]
    public async Task The_title_only_gap_is_live_like_every_other_timing()
    {
        // Same window, same script; the only difference is a PATCH before the loop starts. A
        // shorter gap means more readings, with no restart — which is what lets the attentiveness
        // control scale this alongside the other timings in T004.
        using Harness h = Build(
            options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(20) },
            windowSource: time => new ScriptedSource(time, RetitlingScript()),
            dwell: TimeSpan.FromSeconds(4));
        h.Settings.Update(new System.Text.Json.Nodes.JsonObject
        {
            ["titleRecaptureInterval"] = "00:00:05",
        });

        await h.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => h.Metrics.Snapshot().Observed >= 9, TimeSpan.FromSeconds(10));
        await h.Agent.StopAsync(CancellationToken.None);

        Assert.Equal(9, h.Metrics.Snapshot().Observed); // t = 4, 9, 14 … 44
    }

    [Fact]
    public void Constructor_validates_dependencies()
    {
        var settings = new LiveCaptureSettings(new CaptureOptions());
        Assert.Throws<ArgumentNullException>(() => new CaptureAgent(
            null!, // the live settings ARE the configuration now — there is no options parameter
            new WindowMonitor(new FixedSource(Window), new FakeTimeProvider(), settings),
            new StubExtractor("x"), new SensitivityFilter(settings, new AllowProbe()),
            new CaptureStatusTracker(),
            new ActivityStore(":memory:"), new CaptureMetrics(), new ReadySignal(),
            new FakeTimeProvider(), NullLogger<CaptureAgent>.Instance,
            new Lore.Agent.Capture.Episodes.EpisodeBuilder(settings),
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
