using Lore.Agent.Import;

namespace Lore.Agent.Tests.Import;

/// <summary>Spec 009 T002: jobs can be created, transitioned, and queried. Drives the store through
/// its lifecycle (pending → running → terminal) and its guard rails, with a fake clock so the
/// timestamps it stamps are assertable.</summary>
public sealed class ImportJobStoreTests
{
    private sealed class FakeTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan by) => Now += by;
    }

    [Fact]
    public void Create_starts_a_pending_job_with_distinct_ids_and_stamps()
    {
        var clock = new FakeTimeProvider();
        var store = new ImportJobStore(clock);

        ImportJob job = store.Create("resume.pdf");

        Assert.Equal(ImportJobStatus.Pending, job.Status);
        Assert.Equal("resume.pdf", job.Source);
        Assert.False(string.IsNullOrWhiteSpace(job.Id));
        Assert.False(string.IsNullOrWhiteSpace(job.DocumentId));
        Assert.NotEqual(job.Id, job.DocumentId);
        Assert.Equal(clock.Now, job.CreatedAt);
        Assert.Equal(clock.Now, job.UpdatedAt);
        Assert.Equal(0, job.ProcessedChunks);
        Assert.False(job.IsTerminal);
        Assert.Equal(0.0, job.Progress);
    }

    [Fact]
    public void Each_job_gets_a_unique_id()
    {
        var store = new ImportJobStore(new FakeTimeProvider());

        var ids = Enumerable.Range(0, 100).Select(_ => store.Create("doc.pdf").Id).ToHashSet();

        Assert.Equal(100, ids.Count);
    }

    [Fact]
    public void Get_returns_the_recorded_job_and_null_for_an_unknown_id()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        ImportJob job = store.Create("doc.pdf");

        Assert.Equal(job, store.Get(job.Id));
        Assert.Null(store.Get("job-does-not-exist"));
    }

    [Fact]
    public void Running_then_progress_advances_the_fraction()
    {
        var clock = new FakeTimeProvider();
        var store = new ImportJobStore(clock);
        string id = store.Create("notes.pdf").Id;

        clock.Advance(TimeSpan.FromSeconds(1));
        ImportJob? running = store.MarkRunning(id, totalChunks: 4);
        Assert.NotNull(running);
        Assert.Equal(ImportJobStatus.Running, running!.Status);
        Assert.Equal(0.0, running.Progress);
        Assert.Equal(clock.Now, running.UpdatedAt);

        ImportJob? half = store.ReportProgress(id, processedChunks: 2, memoriesCreated: 3);
        Assert.Equal(0.5, half!.Progress);
        Assert.Equal(3, half.MemoriesCreated);
        Assert.False(half.IsTerminal);
    }

    [Fact]
    public void A_completed_job_reads_as_fully_progressed_and_terminal()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        string id = store.Create("doc.pdf").Id;
        store.MarkRunning(id, totalChunks: 10);
        store.ReportProgress(id, processedChunks: 10, memoriesCreated: 12);

        ImportJob? done = store.MarkCompleted(id);

        Assert.Equal(ImportJobStatus.Completed, done!.Status);
        Assert.True(done.IsTerminal);
        Assert.Equal(1.0, done.Progress);
    }

    [Fact]
    public void A_failed_job_carries_the_error_and_reads_as_terminal()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        string id = store.Create("broken.pdf").Id;
        store.MarkRunning(id, totalChunks: 0);

        ImportJob? failed = store.MarkFailed(id, "could not read the PDF");

        Assert.Equal(ImportJobStatus.Failed, failed!.Status);
        Assert.True(failed.IsTerminal);
        Assert.Equal(1.0, failed.Progress); // terminal jobs read 100% even with no chunks
        Assert.Equal("could not read the PDF", failed.Error);
    }

    [Fact]
    public void A_warning_is_attached_without_changing_status()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        string id = store.Create("scan.pdf").Id;
        store.MarkRunning(id, totalChunks: 1);

        ImportJob? warned = store.Warn(id, "document looks scanned (little extractable text)");

        Assert.Equal(ImportJobStatus.Running, warned!.Status);
        Assert.Equal("document looks scanned (little extractable text)", warned.Warning);
    }

    [Fact]
    public void GetAll_returns_jobs_newest_first()
    {
        var clock = new FakeTimeProvider();
        var store = new ImportJobStore(clock);
        ImportJob first = store.Create("a.pdf");
        clock.Advance(TimeSpan.FromSeconds(1));
        ImportJob second = store.Create("b.pdf");

        IReadOnlyList<ImportJob> all = store.GetAll();

        Assert.Equal([second.Id, first.Id], all.Select(j => j.Id).ToArray());
    }

    [Fact]
    public void Transitions_on_an_unknown_job_return_null()
    {
        var store = new ImportJobStore(new FakeTimeProvider());

        Assert.Null(store.MarkRunning("nope", 1));
        Assert.Null(store.ReportProgress("nope", 1, 1));
        Assert.Null(store.MarkCompleted("nope"));
        Assert.Null(store.MarkFailed("nope", "x"));
        Assert.Null(store.Warn("nope", "x"));
    }

    [Fact]
    public void Negative_counts_are_rejected()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        string id = store.Create("doc.pdf").Id;

        Assert.Throws<ArgumentOutOfRangeException>(() => store.MarkRunning(id, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReportProgress(id, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.ReportProgress(id, 0, -1));
    }

    [Fact]
    public void Create_rejects_a_blank_source()
    {
        var store = new ImportJobStore(new FakeTimeProvider());

        Assert.Throws<ArgumentException>(() => store.Create("  "));
    }

    [Fact]
    public void Concurrent_progress_updates_do_not_tear()
    {
        var store = new ImportJobStore(new FakeTimeProvider());
        string id = store.Create("big.pdf").Id;
        store.MarkRunning(id, totalChunks: 1000);

        // Many threads racing to bump the running total; the CAS loop must not lose updates'
        // coherence — the final read is a valid snapshot within range.
        Parallel.For(0, 1000, i => store.ReportProgress(id, processedChunks: i + 1, memoriesCreated: i + 1));

        ImportJob? final = store.Get(id);
        Assert.NotNull(final);
        Assert.InRange(final!.ProcessedChunks, 1, 1000);
        Assert.Equal(final.ProcessedChunks, final.MemoriesCreated);
    }
}
