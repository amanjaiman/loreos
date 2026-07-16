using Lore.Agent.Capture.Episodes;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture;

/// <summary>The capture loop, hosted in the 002 agent (constitution §3.3). Each tick:
/// poll the foreground window → once dwelled, extract → filter (before anything else) →
/// classify → gate → analyze → <see cref="IMemoryService.RememberAsync"/> → activity log.
/// Filtering happens before any analysis, storage, or egress; the smart gate keeps
/// inference proportional to genuine activity.
///
/// <para>Resilience (acceptance criterion 6): the whole tick is wrapped so one bad window,
/// extraction, or model response never kills the loop, and a memoryd outage is handled
/// gracefully — the observation is dropped with a logged, recoverable warning and the loop
/// keeps running. The loop waits for memoryd's readiness gate before its first tick.</para>
/// </summary>
public sealed class CaptureAgent : BackgroundService
{
    private readonly CaptureOptions _options;
    private readonly WindowMonitor _monitor;
    private readonly ITextExtractor _extractor;
    private readonly SensitivityFilter _filter;
    private readonly SmartGate _gate;
    private readonly CaptureAnalyzer _analyzer;
    private readonly IMemoryService _memory;
    private readonly ActivityStore _activity;
    private readonly CaptureMetrics _metrics;
    private readonly IReadinessSignal _readiness;
    private readonly TimeProvider _time;
    private readonly ILogger<CaptureAgent> _logger;
    private readonly EpisodeBuilder _episodes;
    private readonly IEpisodeProcessor _episodeProcessor;

    private string _lastProcessedKey = string.Empty;
    private DateTimeOffset _lastProcessedAt = DateTimeOffset.MinValue;

    public CaptureAgent(
        CaptureOptions options,
        WindowMonitor monitor,
        ITextExtractor extractor,
        SensitivityFilter filter,
        SmartGate gate,
        CaptureAnalyzer analyzer,
        IMemoryService memory,
        ActivityStore activity,
        CaptureMetrics metrics,
        IReadinessSignal readiness,
        TimeProvider time,
        ILogger<CaptureAgent> logger,
        EpisodeBuilder episodes,
        IEpisodeProcessor episodeProcessor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(episodeProcessor);
        _options = options;
        _monitor = monitor;
        _extractor = extractor;
        _filter = filter;
        _gate = gate;
        _analyzer = analyzer;
        _memory = memory;
        _activity = activity;
        _metrics = metrics;
        _readiness = readiness;
        _time = time;
        _logger = logger;
        _episodes = episodes;
        _episodeProcessor = episodeProcessor;
    }

    private bool IsV2 => string.Equals(_options.Pipeline, "v2", StringComparison.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("capture is disabled by configuration; the loop will not run");
            return;
        }

        try
        {
            await _readiness.WaitUntilReadyAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // shutting down before memory was ready
        }

        _logger.LogInformation("capture loop started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WindowObservation observation = _monitor.Poll();
                if (observation.HasDwelled && ShouldProcess(observation.Window))
                {
                    await CaptureOnceAsync(observation.Window, stoppingToken).ConfigureAwait(false);
                }

                if (IsV2)
                {
                    Episode? idle = _episodes.CloseIfIdle(_time.GetUtcNow());
                    if (idle is not null)
                    {
                        await HandleClosedEpisodeAsync(idle, "idle_timeout", stoppingToken).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // one bad tick must never kill the loop (criterion 6)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "capture tick failed; continuing");
            }

            await DelaySafe(_options.PollInterval, stoppingToken).ConfigureAwait(false);
        }

        // Shutdown flush: the day's last episode is distilled, not lost (v2-001 T004).
        if (IsV2)
        {
            Episode? last = _episodes.Flush();
            if (last is not null)
            {
                await HandleClosedEpisodeAsync(last, "shutdown_flush", CancellationToken.None).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("capture loop stopped");
    }

    /// <summary>Run one window through the full pipeline. Returns what happened; never
    /// throws for an ordinary failure (a memoryd outage is logged and recoverable).</summary>
    internal async Task<CaptureOutcome> CaptureOnceAsync(WindowSnapshot window, CancellationToken cancellationToken)
    {
        ExtractedText extracted = await _extractor.ExtractAsync(window, cancellationToken).ConfigureAwait(false);

        // Filter before anything else looks at the text (constitution §4.4).
        FilterResult filtered = _filter.Apply(window, extracted.Text);
        if (filtered.Blocked)
        {
            _metrics.Filtered(filtered.Reason);
            await LogActivityAsync(window, ActivityDecision.Filtered, filtered.Reason.ToString(), cancellationToken)
                .ConfigureAwait(false);
            return CaptureOutcome.Filtered;
        }

        ContentType type = ContentClassifier.Classify(window, filtered.Text);

        if (IsV2)
        {
            return await ObserveForEpisodeAsync(window, type, filtered.Text, cancellationToken)
                .ConfigureAwait(false);
        }

        GateDecision gate = _gate.Evaluate(window, type, filtered.Text);
        if (!gate.ShouldCapture)
        {
            _metrics.Skipped(gate.Reason);
            return CaptureOutcome.Skipped;
        }

        CaptureAnalysis? analysis =
            await _analyzer.AnalyzeAsync(window.Title, filtered.Text, cancellationToken).ConfigureAwait(false);
        if (analysis is null)
        {
            _metrics.AnalysisEmpty();
            return CaptureOutcome.AnalysisEmpty;
        }

        try
        {
            await _memory.RememberAsync(
                analysis.Observation,
                metadata: new Dictionary<string, object?> { ["category"] = analysis.Category },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // a memoryd outage must be recoverable, not fatal (criterion 6)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "could not store observation; memoryd may be restarting — dropping this one");
            _metrics.MemoryError();
            return CaptureOutcome.MemoryError;
        }

        _gate.Record(window, type, filtered.Text);
        await LogCaptureAsync(window, type, extracted.Source, filtered.Text, analysis, cancellationToken)
            .ConfigureAwait(false);
        _metrics.Captured();
        return CaptureOutcome.Captured;
    }

    // The v2 tail of one capture: the filtered observation joins the episode stream; a
    // closed episode is persisted, logged, and handed to the distiller/lifecycle seam.
    private async Task<CaptureOutcome> ObserveForEpisodeAsync(
        WindowSnapshot window, ContentType type, string text, CancellationToken cancellationToken)
    {
        var observation = new CapturedObservation(
            _time.GetUtcNow(), window.ProcessExecutable, window.Title, text, type);
        Episode? closed = _episodes.Add(observation);
        _metrics.Captured();
        if (closed is null)
        {
            return CaptureOutcome.Observed;
        }

        await HandleClosedEpisodeAsync(closed, "continuity_break_or_bound", cancellationToken)
            .ConfigureAwait(false);
        return CaptureOutcome.EpisodeClosed;
    }

    private async Task HandleClosedEpisodeAsync(
        Episode episode, string reason, CancellationToken cancellationToken)
    {
        await _activity.SaveEpisodeAsync(episode, cancellationToken).ConfigureAwait(false);
        await _activity.LogDecisionAsync(
            new DecisionEntry(
                _time.GetUtcNow(), episode.Id, "closed", reason,
                string.Empty, string.Empty, string.Empty),
            cancellationToken).ConfigureAwait(false);
        try
        {
            await _episodeProcessor.ProcessAsync(episode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // a bad distill/lifecycle round never kills the loop (criterion 6)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "episode {Id} processing failed; episode is persisted", episode.Id);
        }
    }

    // Process a window when it's newly in front, or when the re-capture interval has passed
    // for the same window — so a window held in focus isn't re-extracted on every poll.
    private bool ShouldProcess(WindowSnapshot window)
    {
        string key = window.Handle + "" + window.Title;
        DateTimeOffset now = _time.GetUtcNow();
        if (key != _lastProcessedKey || now - _lastProcessedAt >= _options.RecaptureInterval)
        {
            _lastProcessedKey = key;
            _lastProcessedAt = now;
            return true;
        }

        return false;
    }

    private Task LogCaptureAsync(
        WindowSnapshot window, ContentType type, ExtractionSource source, string text,
        CaptureAnalysis analysis, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        Task activity = _activity.LogActivityAsync(
            new ActivityLogEntry(
                now, window.ProcessExecutable, window.Title, ActivityDecision.Captured,
                string.Empty, analysis.Observation, analysis.Category),
            cancellationToken);
        Task raw = _activity.LogRawCaptureAsync(
            new RawCaptureEntry(
                now, window.ProcessExecutable, window.Title, source.ToString(), type.ToString(), text),
            cancellationToken);
        return Task.WhenAll(activity, raw);
    }

    private Task LogActivityAsync(
        WindowSnapshot window, ActivityDecision decision, string reason, CancellationToken cancellationToken) =>
        _activity.LogActivityAsync(
            new ActivityLogEntry(
                _time.GetUtcNow(), window.ProcessExecutable, window.Title, decision, reason,
                string.Empty, string.Empty),
            cancellationToken);

    private static async Task DelaySafe(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
