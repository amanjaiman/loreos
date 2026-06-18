using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Capture;
using Lore.Agent.Import;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 009 T004: <c>POST /import</c> starts a background job and returns immediately;
/// <c>GET /import/{id}</c> reports progress to completion (acceptance criterion 2). Exercised
/// end-to-end over a live loopback host, with extraction stubbed so a real PDF file isn't needed.</summary>
public sealed class ImportEndpointsTests
{
    private sealed class StubExtractor : IPdfExtractor
    {
        private readonly PdfExtractionResult _result;

        public StubExtractor(string text, bool lowText = false) =>
            _result = new PdfExtractionResult(text, 1, lowText);

        public PdfExtractionResult Extract(Stream pdf) => _result;
    }

    private sealed class AllowProbe : IWindowSecurityProbe
    {
        public bool HasProtectedContent(WindowSnapshot window) => false;
    }

    private static Task<LoreApiHarness> StartAsync(FakeMemoryService memory, IPdfExtractor extractor) =>
        LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton<IMemoryService>(memory);
                services.AddSingleton(extractor); // registered first so AddDocumentImport's TryAdd defers
                services.AddSingleton(new SensitivityFilter(
                    new Blocklist(Array.Empty<string>(), Array.Empty<string>()), new AllowProbe()));
                services.AddDocumentImport();
            },
            app => app.MapImportEndpoints());

    [Fact]
    public async Task Empty_body_is_rejected_with_400()
    {
        await using LoreApiHarness harness = await StartAsync(new FakeMemoryService(), new StubExtractor("x"));

        HttpResponseMessage response = await harness.Client.PostAsync(new Uri("/import", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_file_is_rejected_with_400()
    {
        await using LoreApiHarness harness = await StartAsync(new FakeMemoryService(), new StubExtractor("x"));

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            new Uri("/import", UriKind.Relative),
            new { path = Path.Combine(Path.GetTempPath(), $"does-not-exist-{Guid.NewGuid():N}.pdf") });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Status_for_an_unknown_job_is_404()
    {
        await using LoreApiHarness harness = await StartAsync(new FakeMemoryService(), new StubExtractor("x"));

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/import/job-nope", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_document_imports_as_a_background_job_with_progress_to_completion()
    {
        var memory = new FakeMemoryService();
        var extractor = new StubExtractor(
            string.Join(' ', Enumerable.Repeat("I live in Seattle and enjoy hiking on the weekends.", 30)));
        await using LoreApiHarness harness = await StartAsync(memory, extractor);

        string path = await WriteTempFileAsync();
        try
        {
            // POST returns immediately with an accepted (not finished) job.
            HttpResponseMessage start = await harness.Client.PostAsJsonAsync(
                new Uri("/import", UriKind.Relative), new { path, source = "diary.pdf" });

            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            using JsonDocument accepted = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            string id = accepted.RootElement.GetProperty("id").GetString()!;
            string documentId = accepted.RootElement.GetProperty("document_id").GetString()!;
            Assert.Equal("diary.pdf", accepted.RootElement.GetProperty("source").GetString());
            Assert.False(string.IsNullOrWhiteSpace(id));

            // GET reports progress; poll to the terminal state.
            JsonElement done = await PollUntilTerminalAsync(harness.Client, id);
            Assert.Equal("completed", done.GetProperty("status").GetString());
            Assert.Equal(1.0, done.GetProperty("progress").GetDouble());
            Assert.True(done.GetProperty("total_chunks").GetInt32() > 1);
            Assert.Equal(
                done.GetProperty("total_chunks").GetInt32(),
                done.GetProperty("processed_chunks").GetInt32());
            Assert.True(done.GetProperty("memories_created").GetInt32() > 0);

            // The memories landed, grouped under this import's document id (criterion 4).
            IReadOnlyList<MemoryRecord> stored = await memory.GetAllAsync();
            Assert.NotEmpty(stored);
            Assert.All(stored, record =>
                Assert.Equal(documentId, record.Metadata!["document_id"].GetString()));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Source_defaults_to_the_file_name()
    {
        var memory = new FakeMemoryService();
        await using LoreApiHarness harness = await StartAsync(memory, new StubExtractor("I drink tea every morning."));

        string path = await WriteTempFileAsync();
        try
        {
            HttpResponseMessage start = await harness.Client.PostAsJsonAsync(
                new Uri("/import", UriKind.Relative), new { path });

            Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
            using JsonDocument doc = JsonDocument.Parse(await start.Content.ReadAsStringAsync());
            Assert.Equal(Path.GetFileName(path), doc.RootElement.GetProperty("source").GetString());

            // Let the background job finish reading before the file is removed.
            await PollUntilTerminalAsync(harness.Client, doc.RootElement.GetProperty("id").GetString()!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WriteTempFileAsync()
    {
        // The stub extractor ignores the bytes; the endpoint only needs the path to exist.
        string path = Path.Combine(Path.GetTempPath(), $"lore-import-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "stub document content");
        return path;
    }

    private static async Task<JsonElement> PollUntilTerminalAsync(HttpClient client, string id)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            HttpResponseMessage response = await client.GetAsync(new Uri($"/import/{id}", UriKind.Relative));
            response.EnsureSuccessStatusCode();
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            string status = doc.RootElement.GetProperty("status").GetString()!;
            if (status is "completed" or "failed")
            {
                return doc.RootElement.Clone();
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"import job '{id}' did not finish in time");
    }
}
