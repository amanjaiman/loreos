using System.Diagnostics;
using Lore.Agent.Capture;
using Lore.Agent.Inference;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Capture;

public sealed class CaptureAgentTests
{
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

    private sealed class StubBackend : IInferenceBackend
    {
        private readonly string? _response;

        public StubBackend(string? response) => _response = response;

        public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(_response);
    }

    private sealed class ReadySignal : IReadinessSignal
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeMemoryService : IMemoryService
    {
        private readonly bool _throwOnRemember;

        public FakeMemoryService(bool throwOnRemember = false) => _throwOnRemember = throwOnRemember;

        public List<(string Observation, IReadOnlyDictionary<string, object?>? Metadata)> Remembered { get; } = [];

        public Task<IReadOnlyList<AddedMemory>> RememberAsync(
            string observation, string userId = "default",
            IReadOnlyDictionary<string, object?>? metadata = null, CancellationToken cancellationToken = default)
        {
            if (_throwOnRemember)
            {
                throw new MemorydException("memoryd is down");
            }

            Remembered.Add((observation, metadata));
            return Task.FromResult<IReadOnlyList<AddedMemory>>([new AddedMemory("1", observation, "ADD")]);
        }

        public Task<IReadOnlyList<MemoryRecord>> SearchAsync(
            string query, string userId = "default", int limit = 10,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<IReadOnlyList<MemoryRecord>> ListAsync(
            string userId = "default", int limit = 100, int offset = 0,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
            string userId = "default", int count = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
            string userId = "default", CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MemoryRecord>>([]);

        public Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<MemoryRecord?>(null);

        public Task<MemoryRecord?> UpdateAsync(
            string id, string? text = null,
            IReadOnlyDictionary<string, object?>? metadataPatch = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<MemoryRecord?>(null);

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class Harness : IDisposable
    {
        public required FakeMemoryService Memory { get; init; }
        public required ActivityStore Activity { get; init; }
        public required CaptureMetrics Metrics { get; init; }
        public required CaptureAgent Agent { get; init; }

        public void Dispose() => Activity.Dispose();
    }

    private static readonly WindowSnapshot Window = new(1, "editor", "Designing Data-Intensive Apps");

    private static Harness Build(
        string extractedText = "chapter five on replication",
        string? modelResponse = """{"observation": "I'm reading about replication", "category": "reading"}""",
        bool memoryThrows = false,
        IEnumerable<string>? blockedApps = null,
        CaptureOptions? options = null)
    {
        var time = new FakeTimeProvider();
        var memory = new FakeMemoryService(memoryThrows);
        var activity = new ActivityStore(":memory:");
        var metrics = new CaptureMetrics();
        var filter = new SensitivityFilter(
            new Blocklist(blockedApps ?? [], []), new AllowProbe());
        var gate = new SmartGate(new SmartGateOptions(), new RecentCaptureGate(20), time);
        var analyzer = new CaptureAnalyzer(new StubBackend(modelResponse));
        var monitor = new WindowMonitor(new FixedSource(Window), time, TimeSpan.Zero);

        var agent = new CaptureAgent(
            options ?? new CaptureOptions(),
            monitor,
            new StubExtractor(extractedText),
            filter,
            gate,
            analyzer,
            memory,
            activity,
            metrics,
            new ReadySignal(),
            time,
            NullLogger<CaptureAgent>.Instance);

        return new Harness { Memory = memory, Activity = activity, Metrics = metrics, Agent = agent };
    }

    private sealed class FixedSource : IForegroundWindowSource
    {
        private readonly WindowSnapshot _window;

        public FixedSource(WindowSnapshot window) => _window = window;

        public WindowSnapshot Current() => _window;
    }

    // ── Acceptance criterion 1: end-to-end capture produces a stored memory ───────

    [Fact]
    public async Task End_to_end_capture_stores_a_distilled_memory()
    {
        using Harness h = Build();

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Captured, outcome);
        (string observation, IReadOnlyDictionary<string, object?>? metadata) = Assert.Single(h.Memory.Remembered);
        Assert.Equal("I'm reading about replication", observation);
        Assert.Equal("reading", metadata!["category"]);
        Assert.Equal(1, h.Metrics.Snapshot().Captured);
    }

    [Fact]
    public async Task A_capture_is_written_to_the_local_activity_log_and_raw_captures()
    {
        using Harness h = Build();

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        ActivityLogEntry activity = Assert.Single(await h.Activity.GetRecentActivityAsync());
        Assert.Equal(ActivityDecision.Captured, activity.Decision);
        Assert.Equal("I'm reading about replication", activity.Observation);
        RawCaptureEntry raw = Assert.Single(await h.Activity.GetRecentRawCapturesAsync());
        Assert.Equal("chapter five on replication", raw.Text);
        Assert.Equal("UiAutomation", raw.ExtractionSource);
    }

    // ── Acceptance criterion 6: a memoryd outage is recoverable, not fatal ────────

    [Fact]
    public async Task A_memoryd_outage_is_logged_and_recoverable()
    {
        using Harness h = Build(memoryThrows: true);

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.MemoryError, outcome); // did not throw
        Assert.Equal(1, h.Metrics.Snapshot().MemoryErrors);
        Assert.Empty(await h.Activity.GetRecentActivityAsync()); // nothing logged as captured
    }

    // ── Each decision is filtered/skipped/analyzed correctly ──────────────────────

    [Fact]
    public async Task Blocklisted_content_is_filtered_before_analysis()
    {
        using Harness h = Build(blockedApps: ["editor"]);

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Filtered, outcome);
        Assert.Empty(h.Memory.Remembered); // never reached the model or memory
        Assert.Equal(1, h.Metrics.Snapshot().Filtered[FilterReason.BlockedApp]);
        ActivityLogEntry logged = Assert.Single(await h.Activity.GetRecentActivityAsync());
        Assert.Equal(ActivityDecision.Filtered, logged.Decision);
        Assert.Equal("BlockedApp", logged.Reason);
    }

    [Fact]
    public async Task An_unchanged_window_is_skipped_by_the_gate()
    {
        using Harness h = Build();

        await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);       // captured
        CaptureOutcome second = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.Skipped, second);
        Assert.Single(h.Memory.Remembered); // only the first produced a memory
        Assert.Equal(1, h.Metrics.Snapshot().Skipped[SkipReason.Unchanged]);
    }

    [Fact]
    public async Task Empty_model_output_is_a_skip_not_a_capture()
    {
        using Harness h = Build(modelResponse: """{"observation": "", "category": ""}""");

        CaptureOutcome outcome = await h.Agent.CaptureOnceAsync(Window, CancellationToken.None);

        Assert.Equal(CaptureOutcome.AnalysisEmpty, outcome);
        Assert.Empty(h.Memory.Remembered);
        Assert.Equal(1, h.Metrics.Snapshot().AnalysisEmpty);
    }

    // ── The hosted loop runs end to end ───────────────────────────────────────────

    [Fact]
    public async Task The_hosted_loop_polls_and_stores_a_memory_then_stops()
    {
        using Harness h = Build(options: new CaptureOptions { PollInterval = TimeSpan.FromMilliseconds(10) });

        await h.Agent.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => h.Memory.Remembered.Count >= 1, TimeSpan.FromSeconds(5));
        await h.Agent.StopAsync(CancellationToken.None);

        Assert.NotEmpty(h.Memory.Remembered);
    }

    [Fact]
    public async Task A_disabled_loop_captures_nothing()
    {
        using Harness h = Build(options: new CaptureOptions { Enabled = false });

        await h.Agent.StartAsync(CancellationToken.None);
        await Task.Delay(50);
        await h.Agent.StopAsync(CancellationToken.None);

        Assert.Empty(h.Memory.Remembered);
    }

    [Fact]
    public void Constructor_validates_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new CaptureAgent(
            null!,
            new WindowMonitor(new FixedSource(Window), new FakeTimeProvider(), TimeSpan.Zero),
            new StubExtractor("x"), new SensitivityFilter(Blocklist.Empty, new AllowProbe()),
            new SmartGate(new SmartGateOptions(), new RecentCaptureGate(1), new FakeTimeProvider()),
            new CaptureAnalyzer(new StubBackend(null)), new FakeMemoryService(), new ActivityStore(":memory:"),
            new CaptureMetrics(), new ReadySignal(), new FakeTimeProvider(), NullLogger<CaptureAgent>.Instance));
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
