using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The memory HTTP surface (spec 005 T002): <c>/memories</c> list/search/get/add/
/// update/delete, every call delegating to <see cref="IMemoryService"/> — the one seam to the
/// store (constitution §3.2). The wire DTOs are Lore's vocabulary with explicit snake_case
/// names, so the contract surfaces (006/007/010) pin to is fixed here and doesn't drift with a
/// serializer default.</summary>
public static class MemoryEndpoints
{
    /// <summary>The default memory owner. Lore is single-user today; <c>user_id</c> is carried
    /// through the contract so a future multi-user surface needs no breaking change.</summary>
    public const string DefaultUserId = "default";

    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Map the memory endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapMemoryEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder memories = app.MapGroup("/memories");

        // List, paged over the full store (memoryd has no native cursor; a local personal
        // store is small enough to page in the API). kind/status filter the library view
        // (v2-001): e.g. ?status=staged is the staging review, ?kind=state the health card.
        memories.MapGet("", async (
            [FromServices] IMemoryService service, string? userId, int? limit, int? offset,
            string? kind, string? status, CancellationToken ct) =>
        {
            int take = NormalizeLimit(limit, fallback: 50);
            int skip = offset is > 0 ? offset.Value : 0;
            if (kind is not null && !MemoryKinds.IsKnown(kind))
            {
                return Results.BadRequest(new ErrorResponse(
                    $"unknown kind '{kind}'; valid: {string.Join(", ", MemoryKinds.All)}"));
            }

            if (status is not null && !MemoryStatuses.IsKnown(status))
            {
                return Results.BadRequest(new ErrorResponse(
                    $"unknown status '{status}'; valid: staged, active, archived"));
            }

            IReadOnlyList<MemoryRecord> all = kind is null && status is null
                ? await service.GetAllAsync(UserOr(userId), ct).ConfigureAwait(false)
                : await ListAllFilteredAsync(service, UserOr(userId), kind, status, ct)
                    .ConfigureAwait(false);
            IEnumerable<MemoryDto> page = all.Skip(skip).Take(take).Select(MemoryDto.From);
            return Results.Json(
                new PagedMemories(page.ToArray(), all.Count, take, skip), ResponseJson);
        });

        memories.MapPost("/search", async (
            SearchRequest? request, [FromServices] IMemoryService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Query))
            {
                return Results.BadRequest(new ErrorResponse("query is required"));
            }

            IReadOnlyList<MemoryRecord> hits;
            try
            {
                hits = await service
                    .SearchAsync(
                        request.Query,
                        UserOr(request.UserId),
                        NormalizeLimit(request.Limit, 10),
                        request.FiltersAsObjects(),
                        ct)
                    .ConfigureAwait(false);
            }
            catch (MemorydException ex) when (ex.StatusCode == StatusCodes.Status400BadRequest)
            {
                // An unsupported filter shape — the store's guidance is the actionable part.
                return Results.BadRequest(new ErrorResponse(ex.Body ?? "unsupported filters"));
            }

            return Results.Json(new MemoryResults(hits.Select(MemoryDto.From).ToArray()), ResponseJson);
        });

        memories.MapGet("/{id}", async (string id, [FromServices] IMemoryService service, CancellationToken ct) =>
        {
            MemoryRecord? record = await service.GetAsync(id, ct).ConfigureAwait(false);
            return record is null
                ? NotFound(id)
                : Results.Json(MemoryDto.From(record), ResponseJson);
        });

        memories.MapPost("", async (AddRequest? request, [FromServices] IMemoryService service, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request?.Text))
            {
                return Results.BadRequest(new ErrorResponse("text is required"));
            }

            IReadOnlyList<AddedMemory> added = await service
                .RememberAsync(request.Text, UserOr(request.UserId), request.MetadataAsObjects(), ct)
                .ConfigureAwait(false);
            return Results.Json(
                new AddedMemories(added.Select(AddedMemoryDto.From).ToArray()), ResponseJson, statusCode: StatusCodes.Status201Created);
        });

        // User authority (v2-001): any user patch outranks inferred evidence — the
        // lifecycle engine may reinforce but never revise or archive it afterwards.
        memories.MapPatch("/{id}", async (
            string id, UpdateRequest? request, [FromServices] IMemoryService service,
            CancellationToken ct) =>
        {
            bool hasText = !string.IsNullOrWhiteSpace(request?.Text);
            if (request is null || (!hasText && request.Pinned is null && request.Kind is null))
            {
                return Results.BadRequest(new ErrorResponse("provide text, pinned, and/or kind"));
            }

            if (request.Kind is not null && !MemoryKinds.IsKnown(request.Kind))
            {
                return Results.BadRequest(new ErrorResponse(
                    $"unknown kind '{request.Kind}'; valid: {string.Join(", ", MemoryKinds.All)}"));
            }

            var patch = new Dictionary<string, object?>
            {
                ["updated_reason"] = MemoryUpdateReasons.UserEdit,
            };
            if (hasText)
            {
                patch["user_edited"] = true;
                patch["confidence"] = 1.0; // the user said so
            }

            if (request.Pinned is bool pinned)
            {
                patch["pinned"] = pinned;
            }

            if (request.Kind is not null)
            {
                patch["kind"] = request.Kind;
            }

            MemoryRecord? updated = await service
                .UpdateAsync(id, hasText ? request.Text : null, patch, ct)
                .ConfigureAwait(false);
            return updated is null
                ? NotFound(id)
                : Results.Json(MemoryDto.From(updated), ResponseJson);
        });

        memories.MapDelete("/{id}", async (string id, [FromServices] IMemoryService service, CancellationToken ct) =>
        {
            bool deleted = await service.DeleteAsync(id, ct).ConfigureAwait(false);
            return deleted ? Results.NoContent() : NotFound(id);
        });

        return app;
    }

    private static string UserOr(string? userId) =>
        string.IsNullOrWhiteSpace(userId) ? DefaultUserId : userId;

    // Page through the seam's filtered list until a short page — mirrors GetAllAsync's
    // enumeration so filtered pages report an honest total at personal-store scale.
    private static async Task<IReadOnlyList<MemoryRecord>> ListAllFilteredAsync(
        IMemoryService service, string userId, string? kind, string? status, CancellationToken ct)
    {
        var filters = new Dictionary<string, object?>();
        if (kind is not null)
        {
            filters["kind"] = kind;
        }

        if (status is not null)
        {
            filters["status"] = status;
        }

        const int PageSize = 500;
        var all = new List<MemoryRecord>();
        for (int offset = 0; ; offset += PageSize)
        {
            IReadOnlyList<MemoryRecord> page = await service
                .ListAsync(userId, PageSize, offset, filters, ct).ConfigureAwait(false);
            all.AddRange(page);
            if (page.Count < PageSize)
            {
                return all;
            }
        }
    }

    // A non-positive or absent limit falls back; an oversized one is clamped so a client can't
    // ask the store for an unbounded page.
    private static int NormalizeLimit(int? limit, int fallback) =>
        limit is null or <= 0 ? fallback : Math.Min(limit.Value, 1000);

    private static IResult NotFound(string id) =>
        Results.Json(new ErrorResponse($"no memory with id '{id}'"), ResponseJson, statusCode: StatusCodes.Status404NotFound);
}

/// <summary>A stored memory on the wire. Mirrors <see cref="MemoryRecord"/> in Lore's
/// vocabulary; <c>score</c>/<c>metadata</c>/timestamps are omitted when absent.</summary>
public sealed record MemoryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("memory")] string Memory,
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, JsonElement>? Metadata,
    [property: JsonPropertyName("created_at")] string? CreatedAt,
    [property: JsonPropertyName("updated_at")] string? UpdatedAt)
{
    public static MemoryDto From(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new MemoryDto(
            record.Id, record.Memory, record.Score, record.Metadata, record.CreatedAt, record.UpdatedAt);
    }
}

/// <summary>One outcome of an add: a distilled memory and what happened to it (<c>ADD</c>,
/// <c>UPDATE</c>, or <c>NONE</c>).</summary>
public sealed record AddedMemoryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("memory")] string Memory,
    [property: JsonPropertyName("event")] string Event)
{
    public static AddedMemoryDto From(AddedMemory added)
    {
        ArgumentNullException.ThrowIfNull(added);
        return new AddedMemoryDto(added.Id, added.Memory, added.Event);
    }
}

/// <summary>A page of memories from <c>GET /memories</c>. <c>total</c> is the full count;
/// <c>limit</c>/<c>offset</c> echo the applied window.</summary>
public sealed record PagedMemories(
    [property: JsonPropertyName("items")] IReadOnlyList<MemoryDto> Items,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("limit")] int Limit,
    [property: JsonPropertyName("offset")] int Offset);

/// <summary>Search hits, most relevant first (each carries a <c>score</c>).</summary>
public sealed record MemoryResults(
    [property: JsonPropertyName("results")] IReadOnlyList<MemoryDto> Results);

/// <summary>The memories produced by one add.</summary>
public sealed record AddedMemories(
    [property: JsonPropertyName("results")] IReadOnlyList<AddedMemoryDto> Results);

/// <summary>A flat error body for 4xx responses.</summary>
public sealed record ErrorResponse(
    [property: JsonPropertyName("error")] string Error);

/// <summary><c>POST /memories/search</c> body. <c>filters</c> (v2-001) is the flat
/// <c>field: value</c> / <c>field: {op: value}</c> shape the store supports; unsupported
/// shapes come back as an actionable 400.</summary>
public sealed record SearchRequest
{
    [JsonPropertyName("query")]
    public string? Query { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("filters")]
    public IReadOnlyDictionary<string, JsonElement>? Filters { get; init; }

    /// <summary>Box the JSON filters for the seam (a <see cref="JsonElement"/> serializes
    /// back to its source JSON, so operator objects survive).</summary>
    public IReadOnlyDictionary<string, object?>? FiltersAsObjects() =>
        Filters?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
}

/// <summary><c>POST /memories</c> body: an observation to remember, plus optional owner and
/// metadata. memoryd extracts/dedupes and returns the resulting memories.</summary>
public sealed record AddRequest
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }

    /// <summary>Box the JSON metadata for the <see cref="IMemoryService"/> seam, which takes
    /// <c>object?</c> values (a <see cref="JsonElement"/> serializes back to its source JSON).</summary>
    public IReadOnlyDictionary<string, object?>? MetadataAsObjects() =>
        Metadata?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
}

/// <summary><c>PATCH /memories/{id}</c> body (v2-001): any of the replacement text, a
/// pin toggle, and a kind correction. Every field is user authority — the patch stamps
/// <c>user_edit</c> and a text edit raises confidence to 1.0.</summary>
public sealed record UpdateRequest
{
    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("pinned")]
    public bool? Pinned { get; init; }

    [JsonPropertyName("kind")]
    public string? Kind { get; init; }
}
