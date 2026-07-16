using Lore.Agent.Capture;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Memory;

/// <summary>The one-time v1 → v2 migration (v2-001, decided in plan.md): rows written
/// before the typed schema (no <c>v</c> key) are stamped with full v2 metadata as
/// <b>archived</b> <c>experience</c> facts — visible in the library's archived view,
/// invisible to recall, and never deleted. Idempotent: stamped rows are skipped, so
/// every start converges; a failure mid-sweep just resumes on the next start.</summary>
public sealed class V1ArchiveSweep : BackgroundService
{
    private readonly IMemoryService _memory;
    private readonly IReadinessSignal _readiness;
    private readonly TimeProvider _time;
    private readonly ILogger<V1ArchiveSweep> _logger;

    public V1ArchiveSweep(
        IMemoryService memory,
        IReadinessSignal readiness,
        TimeProvider time,
        ILogger<V1ArchiveSweep> logger)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _memory = memory;
        _readiness = readiness;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _readiness.WaitUntilReadyAsync(stoppingToken).ConfigureAwait(false);
            int stamped = await SweepAsync(stoppingToken).ConfigureAwait(false);
            if (stamped > 0)
            {
                _logger.LogInformation("archived {Count} v1 memories (read-only, never deleted)", stamped);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down; the sweep resumes on the next start
        }
#pragma warning disable CA1031 // migration must never take the agent down with it
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "v1 archive sweep failed; will retry on next start");
        }
    }

    /// <summary>Stamp every un-versioned row; returns how many were stamped.</summary>
    internal async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MemoryRecord> all = await _memory
            .GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        int stamped = 0;
        foreach (MemoryRecord record in all)
        {
            if (record.Metadata is not null && record.Metadata.ContainsKey("v"))
            {
                continue; // already v2-shaped
            }

            var archive = new MemoryMetadata(
                Kind: MemoryKinds.Experience,
                Status: MemoryStatuses.Archived,
                Confidence: 0.5,
                ExpiresAt: MemoryMetadata.FarFutureUnixSeconds,
                EstablishedAt: _time.GetUtcNow().ToUnixTimeSeconds(),
                UpdatedReason: MemoryUpdateReasons.V1Archive);
            await _memory.PatchMetadataAsync(record.Id, archive.ToDictionary(), cancellationToken)
                .ConfigureAwait(false);
            stamped++;
        }

        return stamped;
    }
}
