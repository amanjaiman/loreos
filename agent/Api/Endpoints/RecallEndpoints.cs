using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Memory;
using Lore.Agent.Recall;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The recall hot path on the wire (v2-001): <c>POST /recall</c>. Designed for
/// per-message calls from MCP/CLI/agent clients — zero generative calls behind it, and
/// an empty <c>results</c> is the common, correct answer.</summary>
public static class RecallEndpoints
{
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEndpointRouteBuilder MapRecallEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/recall", async (
            RecallRequest? request, [FromServices] RecallService recall, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Query))
            {
                return Results.BadRequest(new ErrorResponse("query is required"));
            }

            if (request.Kinds is not null
                && request.Kinds.Any(kind => !MemoryKinds.IsKnown(kind)))
            {
                return Results.BadRequest(new ErrorResponse(
                    $"unknown kind; valid kinds: {string.Join(", ", MemoryKinds.All)}"));
            }

            IReadOnlyList<RecallHit> hits = await recall
                .RecallAsync(request.Query, request.K, request.Kinds, ct)
                .ConfigureAwait(false);
            return Results.Json(
                new RecallResponse(hits.Select(RecallHitDto.From).ToArray()), ResponseJson);
        });

        return app;
    }
}

/// <summary><c>POST /recall</c> body: the user's message (or an agent-built query),
/// how many memories at most, and an optional kind restriction.</summary>
public sealed record RecallRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("k")]
    public int? K { get; init; }

    [JsonPropertyName("kinds")]
    public IReadOnlyList<string>? Kinds { get; init; }
}

/// <summary>One recalled memory on the wire.</summary>
public sealed record RecallHitDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("statement")] string Statement,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("established_at")] long EstablishedAt,
    [property: JsonPropertyName("score")] double Score)
{
    public static RecallHitDto From(RecallHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        return new RecallHitDto(hit.Id, hit.Statement, hit.Kind, hit.EstablishedAt, hit.Score);
    }
}

/// <summary>Recall results, strongest first; often — correctly — empty.</summary>
public sealed record RecallResponse(
    [property: JsonPropertyName("results")] IReadOnlyList<RecallHitDto> Results);
