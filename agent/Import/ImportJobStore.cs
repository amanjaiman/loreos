using System.Collections.Concurrent;

namespace Lore.Agent.Import;

/// <summary>Tracks document-import jobs and their progress (spec 009 T002). Deliberately
/// <b>in-memory</b>: a job's work runs in this process, so if the agent restarts the in-flight work
/// is gone and a persisted "running" row would be a lie forever. Status is live runtime state a
/// client polls (<c>GET /import/{id}</c>, 009 T004) while the agent is up — not durable user data
/// (that's memory, behind <see cref="Lore.Agent.Memory.IMemoryService"/>).
///
/// <para>Each job is an immutable <see cref="ImportJob"/>; transitions swap in a new snapshot under
/// a lock-free compare-and-set, so concurrent progress updates never tear and a reader always sees
/// a coherent job. The store owns identity (job id + document id) and stamps timestamps from an
/// injected <see cref="TimeProvider"/> so tests are deterministic.</para></summary>
public sealed class ImportJobStore
{
    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;

    public ImportJobStore(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>Create a pending job for <paramref name="source"/> (a document name/path). Mints a
    /// fresh job id and the document id its memories will be grouped under, and records it.</summary>
    public ImportJob Create(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        DateTimeOffset now = _clock.GetUtcNow();
        var job = new ImportJob(
            Id: NewId("job"),
            DocumentId: NewId("doc"),
            Source: source,
            Status: ImportJobStatus.Pending,
            TotalChunks: 0,
            ProcessedChunks: 0,
            MemoriesCreated: 0,
            Warning: null,
            Error: null,
            CreatedAt: now,
            UpdatedAt: now);

        // Ids are GUID-derived, so a collision is impossible in practice; assert it rather than
        // silently overwrite a live job.
        if (!_jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException($"import job id collision: '{job.Id}'");
        }

        return job;
    }

    /// <summary>The job with <paramref name="id"/>, or <c>null</c> if there is none.</summary>
    public ImportJob? Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _jobs.TryGetValue(id, out ImportJob? job) ? job : null;
    }

    /// <summary>Every job currently tracked, newest first.</summary>
    public IReadOnlyList<ImportJob> GetAll() =>
        _jobs.Values.OrderByDescending(job => job.CreatedAt).ToArray();

    /// <summary>Move a job to <see cref="ImportJobStatus.Running"/> now that its chunk count is
    /// known. Returns the updated job, or <c>null</c> if it no longer exists.</summary>
    public ImportJob? MarkRunning(string id, int totalChunks)
    {
        if (totalChunks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalChunks), totalChunks, "chunk count cannot be negative");
        }

        return Mutate(id, job => job with
        {
            Status = ImportJobStatus.Running,
            TotalChunks = totalChunks,
        });
    }

    /// <summary>Record cumulative progress: how many chunks have been processed and how many
    /// memories they produced so far. Monotonic — callers report running totals.</summary>
    public ImportJob? ReportProgress(string id, int processedChunks, int memoriesCreated)
    {
        if (processedChunks < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processedChunks), processedChunks, "processed count cannot be negative");
        }

        if (memoriesCreated < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoriesCreated), memoriesCreated, "memory count cannot be negative");
        }

        return Mutate(id, job => job with
        {
            ProcessedChunks = processedChunks,
            MemoriesCreated = memoriesCreated,
        });
    }

    /// <summary>Attach a non-fatal warning (e.g. a low-text/scanned PDF) without changing status.</summary>
    public ImportJob? Warn(string id, string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);
        return Mutate(id, job => job with { Warning = warning });
    }

    /// <summary>Mark the job <see cref="ImportJobStatus.Completed"/>.</summary>
    public ImportJob? MarkCompleted(string id) =>
        Mutate(id, job => job with { Status = ImportJobStatus.Completed });

    /// <summary>Mark the job <see cref="ImportJobStatus.Failed"/> with an actionable
    /// <paramref name="error"/>.</summary>
    public ImportJob? MarkFailed(string id, string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        return Mutate(id, job => job with { Status = ImportJobStatus.Failed, Error = error });
    }

    // Compare-and-set: recompute the snapshot from the current one and stamp UpdatedAt, retrying if
    // a concurrent transition slipped in between. Records compare by value, and every snapshot has a
    // fresh UpdatedAt, so TryUpdate's equality check is a true optimistic lock.
    private ImportJob? Mutate(string id, Func<ImportJob, ImportJob> transition)
    {
        ArgumentNullException.ThrowIfNull(id);
        while (_jobs.TryGetValue(id, out ImportJob? current))
        {
            ImportJob updated = transition(current) with { UpdatedAt = _clock.GetUtcNow() };
            if (_jobs.TryUpdate(id, updated, current))
            {
                return updated;
            }
        }

        return null;
    }

    private static string NewId(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("n");
}
