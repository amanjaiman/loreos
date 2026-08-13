using Lore.Agent.Capture.Episodes;
using Lore.Agent.Storage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture;

/// <summary>The capture loop (constitution §3.3), v2-only since the v2-002 reset. Each
/// tick: poll the foreground window → once dwelled, extract → filter (before anything
/// else) → classify → episode intake. Closed episodes are persisted, recorded in the
/// decision trail, and handed to the distiller/lifecycle seam
/// (<see cref="IEpisodeProcessor"/>). Segmentation makes no inference calls.
///
/// <para>Resilience: the whole tick is wrapped so one bad window, extraction, or
/// processing round never kills the loop; a closed episode is persisted before
/// processing so nothing is lost when the model or memoryd misbehaves. The loop waits
/// for memoryd's readiness gate before its first tick and flushes the open episode on
/// shutdown.</para></summary>
public sealed class CaptureAgent : BackgroundService
{
    private readonly CaptureOptions _options;
    private readonly LiveCaptureSettings _settings;
    private readonly WindowMonitor _monitor;
    private readonly ITextExtractor _extractor;
    private readonly SensitivityFilter _filter;
    private readonly CaptureStatusTracker _captureStatus;
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
        LiveCaptureSettings settings,
        WindowMonitor monitor,
        ITextExtractor extractor,
        SensitivityFilter filter,
        CaptureStatusTracker captureStatus,
        ActivityStore activity,
        CaptureMetrics metrics,
        IReadinessSignal readiness,
        TimeProvider time,
        ILogger<CaptureAgent> logger,
        EpisodeBuilder episodes,
        IEpisodeProcessor episodeProcessor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(captureStatus);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(episodes);
        ArgumentNullException.ThrowIfNull(episodeProcessor);
        _options = options;
        _settings = settings;
        _monitor = monitor;
        _extractor = extractor;
        _filter = filter;
        _captureStatus = captureStatus;
        _activity = activity;
        _metrics = metrics;
        _readiness = readiness;
        _time = time;
        _logger = logger;
        _episodes = episodes;
        _episodeProcessor = episodeProcessor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            if (!_settings.Enabled)
            {
                _captureStatus.RecordExcluded();
                await DelaySafe(_options.PollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                WindowObservation observation = _monitor.Poll();
                if (observation.HasDwelled && ShouldProcess(observation.Window))
                {
                    await CaptureOnceAsync(observation.Window, stoppingToken).ConfigureAwait(false);
                }

                Episode? idle = _episodes.CloseIfIdle(_time.GetUtcNow());
                if (idle is not null)
                {
                    await HandleClosedEpisodeAsync(idle, "idle_timeout", stoppingToken).ConfigureAwait(false);
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
        try
        {
            Episode? last = _episodes.Flush();
            if (last is not null)
            {
                await HandleClosedEpisodeAsync(last, "shutdown_flush", CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // a storage failure at shutdown degrades gracefully (criterion 6)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "shutdown flush failed; the open episode could not be persisted");
        }

        _logger.LogInformation("capture loop stopped");
    }

    /// <summary>Run one window through the pipeline: extract → filter → episode intake.
    /// Returns what happened; never throws for an ordinary failure.</summary>
    internal async Task<CaptureOutcome> CaptureOnceAsync(WindowSnapshot window, CancellationToken cancellationToken)
    {
        ExtractedText extracted = await _extractor.ExtractAsync(window, cancellationToken).ConfigureAwait(false);

        // Pause can arrive while UIA/OCR is in flight. Discard that result before it can update
        // status, activity, or an episode so the pause boundary is privacy-safe.
        if (!_settings.Enabled)
        {
            _captureStatus.RecordExcluded();
            return CaptureOutcome.Filtered;
        }

        // Filter before anything else looks at the text (constitution §4.4).
        FilterResult filtered = _filter.Apply(window, extracted.Text);
        if (filtered.Blocked)
        {
            _metrics.Filtered(filtered.Reason);
            // The current window is excluded — drop it from the "watching" signal so a blocklisted
            // title can never surface in the always-visible rail (spec 005 R2, binding rule 1).
            _captureStatus.RecordExcluded();
            await LogActivityAsync(window, ActivityDecision.Filtered, filtered.Reason.ToString(), cancellationToken)
                .ConfigureAwait(false);
            return CaptureOutcome.Filtered;
        }

        DateTimeOffset now = _time.GetUtcNow();

        // The window cleared the whole filter chain, so its title survived the same screening
        // capture uses (blocklist keyword + sensitive pattern) — safe to surface as the redacted
        // "watching" title (spec 005 R2). Only the current window is ever held here.
        _captureStatus.RecordCaptured(window.Title, now);

        ContentType type = ContentClassifier.Classify(window, filtered.Text);
        var observation = new CapturedObservation(
            now, window.ProcessExecutable, window.Title, filtered.Text, type);
        Episode? closed = _episodes.Add(observation);
        _metrics.Observed();
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
        // A closed episode is already off the builder — persist it unconditionally so a
        // cancellation mid-save can't lose it; only processing honors the caller's token.
        await _activity.SaveEpisodeAsync(episode, CancellationToken.None).ConfigureAwait(false);
        await _activity.LogDecisionAsync(
            new DecisionEntry(
                _time.GetUtcNow(), episode.Id, "closed", reason,
                string.Empty, string.Empty, string.Empty),
            CancellationToken.None).ConfigureAwait(false);
        _metrics.EpisodeClosed();
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
        string key = window.Handle + "" + window.Title;
        DateTimeOffset now = _time.GetUtcNow();
        if (key != _lastProcessedKey || now - _lastProcessedAt >= _options.RecaptureInterval)
        {
            _lastProcessedKey = key;
            _lastProcessedAt = now;
            return true;
        }

        return false;
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
