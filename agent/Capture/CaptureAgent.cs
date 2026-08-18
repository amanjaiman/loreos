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
/// shutdown.</para>
///
/// <para>Every setting is read from <see cref="LiveCaptureSettings"/>, once per tick, so a
/// <c>PATCH /config</c> takes effect on the next tick with no restart (v2-008 R2). Taking one
/// snapshot per tick rather than per read is what makes the tick self-consistent: the delay a
/// tick waits out is the delay it decided with.</para></summary>
public sealed class CaptureAgent : BackgroundService
{
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

    private long? _lastProcessedHandle;
    private string _lastProcessedTitle = string.Empty;
    private DateTimeOffset _lastProcessedAt = DateTimeOffset.MinValue;

    public CaptureAgent(
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
            // One read for the whole tick (v2-008 R2): a PATCH landing mid-tick applies from the
            // next one, never halfway through this one.
            CaptureSnapshot settings = _settings.Current;
            if (!settings.Enabled)
            {
                _captureStatus.RecordExcluded();
                await DelaySafe(settings.PollInterval, stoppingToken).ConfigureAwait(false);
                continue;
            }

            try
            {
                WindowObservation observation = _monitor.Poll();
                if (observation.HasDwelled && ShouldProcess(observation.Window, settings))
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

            await DelaySafe(settings.PollInterval, stoppingToken).ConfigureAwait(false);
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
        // status, activity, or an episode so the pause boundary is privacy-safe. This is a FRESH
        // read on purpose — the tick's snapshot predates the extraction, and the whole point here
        // is to honour a setting that changed during it.
        CaptureSnapshot settings = _settings.Current;
        if (!settings.Enabled)
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

        // Opt-in troubleshooting record (v2-008 R4.2), off by default. This sits deliberately
        // BELOW the filter chain: every blocked window returned above, so the only text that can
        // reach this line is `filtered.Text` — post-filter, exactly what episode intake receives.
        // Text the SensitivityFilter dropped is never written here, and turning this on does not
        // widen what Lore records by one character. The table is bounded by RetentionService.
        if (settings.Diagnostics)
        {
            await _activity.LogRawCaptureAsync(
                new RawCaptureEntry(
                    now, window.ProcessExecutable, window.Title,
                    extracted.Source.ToString(), type.ToString(), filtered.Text),
                cancellationToken).ConfigureAwait(false);
        }

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

    // Three cases, cheapest first:
    //
    //   a DIFFERENT window          → read it now. A new thing in front of the user is the whole
    //                                 reason to look, and it has already earned its dwell.
    //   the SAME window, NEW title  → read it once TitleRecaptureInterval has passed.
    //   the SAME window, same title → read it once RecaptureInterval has passed.
    //
    // The middle case is v2-008 R6.2's consequence and the reason this method no longer keys on a
    // concatenated handle+title string. Until R6.2, a window that rewrote its title faster than
    // the dwell threshold never dwelled and so was never captured at all; dwell was accidentally
    // the rate limiter. With that fixed, every retitle was a brand-new key and such a window was
    // re-extracted on EVERY poll — ~1,800 OCR-bearing readings an hour against ~144 for a window
    // that sits still, nearly all of them absorbed downstream as near-duplicates. It must still be
    // captured; it must not be read twelve times more often than everything else. So a title-only
    // change gets its own, shorter gap keyed on the window HANDLE alone.
    private bool ShouldProcess(WindowSnapshot window, CaptureSnapshot settings)
    {
        DateTimeOffset now = _time.GetUtcNow();
        bool sameWindow = _lastProcessedHandle == window.Handle;
        bool sameTitle = sameWindow
            && string.Equals(_lastProcessedTitle, window.Title, StringComparison.Ordinal);
        TimeSpan gap = sameTitle ? settings.RecaptureInterval : settings.TitleRecaptureInterval;

        if (sameWindow && now - _lastProcessedAt < gap)
        {
            return false;
        }

        _lastProcessedHandle = window.Handle;
        _lastProcessedTitle = window.Title;
        _lastProcessedAt = now;
        return true;
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
