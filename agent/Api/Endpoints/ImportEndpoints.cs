using System.IO;
using System.Text.Json.Serialization;
using Lore.Agent.Import;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The document-import HTTP surface (spec 009 T004), filling the route 005 reserved with a
/// 501 placeholder. <c>POST /import</c> starts a background job for a local document and returns its
/// id immediately (202); <c>GET /import/{id}</c> reports status and progress. The work runs off the
/// request thread so a large PDF never blocks the API or the agent loop (acceptance criterion 2,
/// constitution §3.1).</summary>
public static class ImportEndpoints
{
    /// <summary>Map the import endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/import", (
            ImportRequest? request,
            [FromServices] ImportJobStore jobs,
            [FromServices] ImportOptions options,
            [FromServices] IServiceScopeFactory scopes) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Path))
            {
                return Results.BadRequest(new ErrorResponse("path is required"));
            }

            // Validated synchronously so the caller learns of a bad path or an oversized file now
            // (400) rather than via a failed job later. The actual read happens in the background.
            string path = request.Path;
            if (!File.Exists(path))
            {
                return Results.BadRequest(new ErrorResponse($"no file at path '{path}'"));
            }

            long length = new FileInfo(path).Length;
            if (length > options.MaxDocumentBytes)
            {
                return Results.BadRequest(new ErrorResponse(
                    $"document is {length} bytes; the import limit is {options.MaxDocumentBytes} bytes"));
            }

            string source = string.IsNullOrWhiteSpace(request.Source) ? Path.GetFileName(path) : request.Source!;
            ImportJob job = jobs.Create(source);
            RunInBackground(scopes, jobs, job.Id, path);

            // 202 + a Location pointing at the status route: the job is accepted, not finished.
            return Results.Accepted($"/import/{job.Id}", ImportJobDto.From(job));
        });

        app.MapGet("/import/{id}", (string id, [FromServices] ImportJobStore jobs) =>
        {
            ImportJob? job = jobs.Get(id);
            return job is null
                ? Results.Json(new ErrorResponse($"no import job with id '{id}'"), statusCode: StatusCodes.Status404NotFound)
                : Results.Ok(ImportJobDto.From(job));
        });

        return app;
    }

    // Run the import on its own DI scope, detached from the request. The service marks the job
    // failed on a document error; this outer guard catches the file-open path too, so a job never
    // hangs and the loop never sees an unobserved exception (criterion 5; hardened in T005).
    private static void RunInBackground(IServiceScopeFactory scopes, ImportJobStore jobs, string jobId, string path)
    {
        _ = Task.Run(async () =>
        {
            AsyncServiceScope scope = scopes.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                DocumentImportService service = scope.ServiceProvider.GetRequiredService<DocumentImportService>();
                try
                {
                    FileStream stream = File.OpenRead(path);
                    await using (stream.ConfigureAwait(false))
                    {
                        await service.ImportAsync(jobId, stream).ConfigureAwait(false);
                    }
                }
#pragma warning disable CA1031 // background boundary: any failure ends this one job cleanly (criterion 5)
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    string reason = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                    jobs.MarkFailed(jobId, reason);
                }
            }
        });
    }
}

/// <summary><c>POST /import</c> body: the local <c>path</c> of the document to import and an optional
/// display <c>source</c> name (defaults to the file name). The API is loopback-only and single-user,
/// so a same-machine path is the document handle.</summary>
public sealed record ImportRequest
{
    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>An import job on the wire (009 T004): identity, the <c>document_id</c> its memories are
/// grouped under, where it is, and how it ended. Snake-case so the contract surfaces (010) pin to is
/// explicit and serializer-independent.</summary>
public sealed record ImportJobDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("document_id")] string DocumentId,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("progress")] double Progress,
    [property: JsonPropertyName("total_chunks")] int TotalChunks,
    [property: JsonPropertyName("processed_chunks")] int ProcessedChunks,
    [property: JsonPropertyName("memories_created")] int MemoriesCreated,
    [property: JsonPropertyName("warning")] string? Warning,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("updated_at")] string UpdatedAt)
{
    public static ImportJobDto From(ImportJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new ImportJobDto(
            job.Id,
            job.DocumentId,
            job.Source,
            Wire(job.Status),
            job.Progress,
            job.TotalChunks,
            job.ProcessedChunks,
            job.MemoriesCreated,
            job.Warning,
            job.Error,
            job.CreatedAt.ToString("O"),
            job.UpdatedAt.ToString("O"));
    }

    // Explicit lowercase wire values — fixed by the contract, not by the enum's casing or a culture.
    private static string Wire(ImportJobStatus status) => status switch
    {
        ImportJobStatus.Pending => "pending",
        ImportJobStatus.Running => "running",
        ImportJobStatus.Completed => "completed",
        ImportJobStatus.Failed => "failed",
        _ => status.ToString(),
    };
}
