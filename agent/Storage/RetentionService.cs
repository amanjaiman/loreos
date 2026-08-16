using Lore.Agent.Capture;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Storage;

/// <summary>Keeps the local operational store bounded (v2-008 R4). Sweeps once at agent start
/// and once a day thereafter: evidence older than <c>capture.retentionDays</c> goes, and the
/// opt-in <c>raw_captures</c> diagnostic is held to its hard 24-hour / 500-row ceiling.
///
/// <para><b>Memories are never pruned.</b> This service reaches exactly one dependency —
/// <see cref="ActivityStore"/> — which has no path to <c>IMemoryService</c>, so there is no
/// route from here to a memory even by mistake. Retention deletes the evidence; what the
/// evidence supported stays, and the evidence read paths render whatever survives.</para>
///
/// <para>Start-of-day rather than write-time pruning because the cost that matters is disk
/// over months, not rows over minutes: a daily sweep keeps the bound honest without putting a
/// DELETE on the capture loop's hot path. A failed sweep is logged and retried at the next
/// interval — falling behind on retention must never take the agent down.</para></summary>
public sealed class RetentionService : BackgroundService
{
    /// <summary>How long <c>raw_captures</c> rows live, and how many are kept — whichever bound
    /// bites first (R4.2). Not configurable: the point of the diagnostic being safe to switch on
    /// is that the user cannot accidentally leave gigabytes of screen text behind.</summary>
    internal static readonly TimeSpan RawCaptureMaxAge = TimeSpan.FromHours(24);

    internal const int RawCaptureMaxRows = 500;

    /// <summary>The gap between sweeps. Retention is a housekeeping concern measured in days,
    /// so a daily pass is as often as it can usefully run.</summary>
    internal static readonly TimeSpan SweepInterval = TimeSpan.FromDays(1);

    private readonly ActivityStore _store;
    private readonly CaptureOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        ActivityStore store,
        CaptureOptions options,
        TimeProvider time,
        ILogger<RetentionService> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _options = options;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int removed = await SweepAsync(stoppingToken).ConfigureAwait(false);
                if (removed > 0)
                {
                    _logger.LogInformation(
                        "retention pruned {Count} local telemetry rows (memories untouched)", removed);
                }
            }
            catch (OperationCanceledException)
            {
                break; // shutting down
            }
#pragma warning disable CA1031 // housekeeping must never take the agent down with it
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "retention sweep failed; retrying at the next interval");
            }

            try
            {
                await Task.Delay(SweepInterval, _time, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Run one sweep; returns how many rows it removed.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        int removed = 0;

        // retentionDays 0 means keep forever — an explicit user choice, so no prune at all
        // rather than a very long window. A negative value reads the same way.
        if (_options.RetentionDays > 0)
        {
            removed += await _store
                .PruneOlderThanAsync(now.AddDays(-_options.RetentionDays), cancellationToken)
                .ConfigureAwait(false);
        }

        // With diagnostics off the table is emptied, not merely bounded: rows written during an
        // earlier troubleshooting session should not outlive the session that asked for them.
        removed += _options.Diagnostics
            ? await _store
                .PruneRawCapturesAsync(now - RawCaptureMaxAge, RawCaptureMaxRows, cancellationToken)
                .ConfigureAwait(false)
            : await _store
                .PruneRawCapturesAsync(now, maxRows: 0, cancellationToken)
                .ConfigureAwait(false);

        if (removed > 0)
        {
            // Only worth the whole-file rewrite when something actually went; otherwise the
            // daily sweep would rewrite activity.db every day for nothing.
            await _store.VacuumAsync(cancellationToken).ConfigureAwait(false);
        }

        return removed;
    }
}
