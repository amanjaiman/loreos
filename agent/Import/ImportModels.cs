namespace Lore.Agent.Import;

/// <summary>Tunable limits for document import (spec 009 T005). Bound from the <c>import</c> config
/// section; defaults are sensible for a personal machine.</summary>
public sealed class ImportOptions
{
    /// <summary>The largest document the API will accept, in bytes. Oversized files are rejected up
    /// front (so the user gets immediate feedback) rather than flooding memory with low-value chunks
    /// (spec risk). Default: 25 MiB.</summary>
    public long MaxDocumentBytes { get; init; } = 25L * 1024 * 1024;
}

/// <summary>Where a document-import job is in its lifecycle (spec 009 T002). A job starts
/// <see cref="Pending"/>, moves to <see cref="Running"/> once work begins, and ends in exactly one
/// terminal state — <see cref="Completed"/> or <see cref="Failed"/>.</summary>
public enum ImportJobStatus
{
    /// <summary>Created but not yet started.</summary>
    Pending,

    /// <summary>Extraction/filter/chunk/remember is in progress; <see cref="ImportJob.Progress"/>
    /// advances.</summary>
    Running,

    /// <summary>Finished successfully; every surviving chunk was stored.</summary>
    Completed,

    /// <summary>Stopped on an error; <see cref="ImportJob.Error"/> says why. No further work runs.</summary>
    Failed,
}

/// <summary>The full state of one document-import job (spec 009 T002): its identity, the document
/// it imports, where it is, and how it ended. Immutable — the <see cref="ImportJobStore"/> swaps in
/// a new snapshot on every transition, so a status read never sees a half-updated job.
///
/// <para><see cref="DocumentId"/> is the grouping key every memory this job stores carries
/// (009 T003), so an import is listable and forgettable as a unit. <see cref="Warning"/> is a
/// non-fatal note (e.g. a low-text/scanned PDF) that does not fail the job.</para></summary>
public sealed record ImportJob(
    string Id,
    string DocumentId,
    string Source,
    ImportJobStatus Status,
    int TotalChunks,
    int ProcessedChunks,
    int MemoriesCreated,
    string? Warning,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Fractional progress in <c>[0, 1]</c>. Zero while the chunk count is still unknown
    /// (extraction hasn't finished); a terminal job always reads <c>1</c> so a poller sees a
    /// finished job as 100% even if it failed or stored nothing.</summary>
    public double Progress =>
        Status is ImportJobStatus.Completed or ImportJobStatus.Failed
            ? 1.0
            : TotalChunks <= 0 ? 0.0 : Math.Clamp((double)ProcessedChunks / TotalChunks, 0.0, 1.0);

    /// <summary><c>true</c> once the job has reached a terminal state.</summary>
    public bool IsTerminal => Status is ImportJobStatus.Completed or ImportJobStatus.Failed;
}
