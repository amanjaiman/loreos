using System.IO;
using System.Text.Json;
using Lore.Agent.Capture;
using Lore.Agent.Import;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Memory;

namespace Lore.Agent.Tests.Import;

/// <summary>Spec 009 T003: the import service extracts, filters, chunks, and remembers — producing
/// searchable, source-tagged memories (acceptance criteria 1 &amp; 4) while dropping SSN/card content
/// (criterion 3). Extraction is stubbed so the cases drive text straight through the real chunker,
/// the real trust-critical filter, and a fake memory store.</summary>
public sealed class DocumentImportServiceTests
{
    private sealed class StubExtractor : IPdfExtractor
    {
        private readonly PdfExtractionResult _result;

        public StubExtractor(string text, bool lowText = false, int pages = 1) =>
            _result = new PdfExtractionResult(text, pages, lowText);

        public PdfExtractionResult Extract(Stream pdf) => _result;
    }

    private sealed class AllowProbe : IWindowSecurityProbe
    {
        public bool HasProtectedContent(WindowSnapshot window) => false;
    }

    private static DocumentImportService Build(
        IPdfExtractor extractor,
        FakeMemoryService memory,
        ImportJobStore jobs,
        IEnumerable<string>? keywords = null,
        int chunkSize = 80,
        int overlap = 5)
    {
        var filter = new SensitivityFilter(
            new Blocklist(Array.Empty<string>(), keywords ?? Array.Empty<string>()), new AllowProbe());
        return new DocumentImportService(
            extractor, new TextChunker(chunkSize, overlap), filter, memory, jobs);
    }

    [Fact]
    public async Task Import_stores_searchable_source_tagged_memories()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        DocumentImportService service = Build(
            new StubExtractor("I live in Seattle and work as a teacher. My dog Pixel is a husky."),
            memory, jobs);
        ImportJob job = jobs.Create("about-me.pdf");

        ImportJob result = await service.ImportAsync(job.Id, Stream.Null);

        Assert.Equal(ImportJobStatus.Completed, result.Status);
        Assert.Equal(1.0, result.Progress);
        Assert.True(result.MemoriesCreated > 0);

        IReadOnlyList<MemoryRecord> stored = await memory.GetAllAsync();
        Assert.NotEmpty(stored);
        // Every stored memory is tagged with this import's source + document_id (criterion 4).
        Assert.All(stored, record =>
        {
            Assert.NotNull(record.Metadata);
            Assert.Equal("about-me.pdf", record.Metadata!["source"].GetString());
            Assert.Equal(job.DocumentId, record.Metadata["document_id"].GetString());
        });
        // The content is searchable (criterion 1).
        Assert.NotEmpty(await memory.SearchAsync("Seattle"));
    }

    [Fact]
    public async Task Sensitive_chunks_are_filtered_out_but_the_rest_imports()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        // Benign content on both sides spans many chunks; the SSN sits in exactly one chunk
        // (overlap 0, so no neighbor repeats the token), which must be the only one dropped.
        string benignA = Repeat("I live in Seattle and love it here. ", 5);
        string benignB = Repeat("My dog Pixel is a husky always. ", 5);
        string text = benignA + "My number is 123-45-6789 okay. " + benignB;
        DocumentImportService service = Build(new StubExtractor(text), memory, jobs, overlap: 0);
        ImportJob job = jobs.Create("resume.pdf");

        await service.ImportAsync(job.Id, Stream.Null);

        IReadOnlyList<MemoryRecord> stored = await memory.GetAllAsync();
        Assert.All(stored, record =>
            Assert.DoesNotContain("123-45-6789", record.Memory, StringComparison.Ordinal));
        // The benign content on either side of the SSN still made it in.
        Assert.NotEmpty(await memory.SearchAsync("Seattle"));
        Assert.NotEmpty(await memory.SearchAsync("Pixel"));
    }

    [Fact]
    public async Task A_blocklisted_keyword_chunk_is_filtered_out()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        string text =
            Repeat("I live in Portland near the river. ", 5)
            + "My salary was high. "
            + Repeat("I enjoy cycling to work daily. ", 5);
        DocumentImportService service = Build(
            new StubExtractor(text), memory, jobs, keywords: new[] { "salary" }, overlap: 0);
        ImportJob job = jobs.Create("notes.pdf");

        await service.ImportAsync(job.Id, Stream.Null);

        IReadOnlyList<MemoryRecord> stored = await memory.GetAllAsync();
        Assert.All(stored, record =>
            Assert.DoesNotContain("salary", record.Memory, StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(await memory.SearchAsync("Portland"));
        Assert.NotEmpty(await memory.SearchAsync("cycling"));
    }

    private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

    [Fact]
    public async Task Progress_reaches_every_chunk()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        DocumentImportService service = Build(
            new StubExtractor(string.Join(' ', Enumerable.Repeat("alpha bravo charlie delta", 20))),
            memory, jobs);
        ImportJob job = jobs.Create("doc.pdf");

        ImportJob result = await service.ImportAsync(job.Id, Stream.Null);

        Assert.True(result.TotalChunks > 1);
        Assert.Equal(result.TotalChunks, result.ProcessedChunks);
        Assert.Equal(ImportJobStatus.Completed, result.Status);
    }

    [Fact]
    public async Task A_low_text_document_completes_with_a_warning()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        DocumentImportService service = Build(
            new StubExtractor(string.Empty, lowText: true, pages: 3), memory, jobs);
        ImportJob job = jobs.Create("scan.pdf");

        ImportJob result = await service.ImportAsync(job.Id, Stream.Null);

        Assert.Equal(ImportJobStatus.Completed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Warning));
        Assert.Equal(0, result.MemoriesCreated);
        Assert.Empty(await memory.GetAllAsync());
    }

    [Fact]
    public async Task An_extraction_failure_fails_the_job_cleanly()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        var throwing = new ThrowingExtractor("the file is not a readable PDF");
        DocumentImportService service = Build(throwing, memory, jobs);
        ImportJob job = jobs.Create("broken.pdf");

        ImportJob result = await service.ImportAsync(job.Id, Stream.Null);

        Assert.Equal(ImportJobStatus.Failed, result.Status);
        Assert.Equal("the file is not a readable PDF", result.Error);
        Assert.Empty(await memory.GetAllAsync());
    }

    [Fact]
    public async Task Importing_an_unknown_job_throws()
    {
        var memory = new FakeMemoryService();
        var jobs = new ImportJobStore(TimeProvider.System);
        DocumentImportService service = Build(new StubExtractor("text"), memory, jobs);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ImportAsync("job-does-not-exist", Stream.Null));
    }

    private sealed class ThrowingExtractor : IPdfExtractor
    {
        private readonly string _message;

        public ThrowingExtractor(string message) => _message = message;

        public PdfExtractionResult Extract(Stream pdf) => throw new InvalidDataException(_message);
    }
}
