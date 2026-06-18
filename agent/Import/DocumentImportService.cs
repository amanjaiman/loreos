using System.IO;
using Lore.Agent.Capture;
using Lore.Agent.Memory;

namespace Lore.Agent.Import;

/// <summary>Orchestrates one document import end to end (spec 009 T003): extract → filter → chunk →
/// <see cref="IMemoryService.RememberAsync">Remember</see>, advancing the job's progress as it goes.
///
/// <para>Two invariants the constitution pins (and this class exists to honor):
/// <list type="bullet">
/// <item><b>Same trust bar.</b> Every chunk passes the trust-critical sensitivity filter
/// (<see cref="SensitivityFilter.ApplyToText"/>) before storage — a document is just another text
/// source, so SSN/card content is dropped exactly as in capture (§4, acceptance criterion 3).</item>
/// <item><b>One storage door.</b> Chunks reach memory only through <see cref="IMemoryService"/>,
/// each tagged with <c>source</c> + <c>document_id</c> so the import is attributable and forgettable
/// as a unit (§3.2, acceptance criteria 1 &amp; 4).</item>
/// </list>
/// Filtering is per-chunk: a single sensitive passage drops only its own chunk, not the whole
/// document, so a résumé with one SSN still imports everything else.</para></summary>
public sealed class DocumentImportService
{
    /// <summary>The note attached to a job when the document yields little or no extractable text —
    /// almost always a scanned/image-only PDF. Non-fatal: the job still completes (with whatever
    /// text there was), but the user is told why so little came through.</summary>
    internal const string LowTextWarning =
        "the document has little extractable text — it may be scanned or image-only";

    private readonly IPdfExtractor _extractor;
    private readonly TextChunker _chunker;
    private readonly SensitivityFilter _filter;
    private readonly IMemoryService _memory;
    private readonly ImportJobStore _jobs;

    public DocumentImportService(
        IPdfExtractor extractor,
        TextChunker chunker,
        SensitivityFilter filter,
        IMemoryService memory,
        ImportJobStore jobs)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        ArgumentNullException.ThrowIfNull(chunker);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(jobs);
        _extractor = extractor;
        _chunker = chunker;
        _filter = filter;
        _memory = memory;
        _jobs = jobs;
    }

    /// <summary>Run the pipeline for an already-created job (the endpoint mints the job and returns
    /// its id immediately, then calls this in the background). Returns the job's terminal snapshot.
    /// A failure ends the job cleanly as <see cref="ImportJobStatus.Failed"/> with the reason rather
    /// than throwing into an unobserved background task — error specificity and size limits are
    /// hardened in T005.</summary>
    /// <exception cref="ArgumentException">No job with <paramref name="jobId"/> exists.</exception>
    public async Task<ImportJob> ImportAsync(
        string jobId, Stream document, string userId = "default", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ImportJob job = _jobs.Get(jobId)
            ?? throw new ArgumentException($"no import job with id '{jobId}'", nameof(jobId));

        try
        {
            PdfExtractionResult extraction = _extractor.Extract(document);
            if (extraction.LowText)
            {
                _jobs.Warn(jobId, LowTextWarning);
            }

            IReadOnlyList<string> chunks = _chunker.Split(extraction.Text);
            _jobs.MarkRunning(jobId, chunks.Count);

            // Every memory from this import carries the same grouping tags, so the document can be
            // listed and forgotten as a unit later (acceptance criterion 4).
            var metadata = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["source"] = job.Source,
                ["document_id"] = job.DocumentId,
            };

            int processed = 0;
            int created = 0;
            foreach (string chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FilterResult safe = _filter.ApplyToText(chunk);
                if (!safe.Blocked)
                {
                    IReadOnlyList<AddedMemory> added = await _memory
                        .RememberAsync(safe.Text, userId, metadata, cancellationToken)
                        .ConfigureAwait(false);
                    created += added.Count;
                }

                processed++;
                _jobs.ReportProgress(jobId, processed, created);
            }

            return _jobs.MarkCompleted(jobId) ?? job;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a document failure; leave the job as-is and let the caller observe it.
            throw;
        }
#pragma warning disable CA1031 // a malformed file must fail this one job cleanly, never crash the agent (criterion 5)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            string reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            return _jobs.MarkFailed(jobId, reason) ?? job;
        }
    }
}
