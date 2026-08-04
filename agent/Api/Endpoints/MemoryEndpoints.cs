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

        // Composition counts for the Home screen (spec 005 R1): active memories keyed by the five
        // v2-001 kinds, plus staged/archived totals — so the client draws its bar and rail badge
        // without over-fetching full rows to count them in the browser. Computed through the same
        // filtered-list path GET /memories uses, so the numbers agree exactly (acceptance). memoryd
        // exposes no count verb, so this pages the (personal-scale) store per status rather than
        // materialising every row twice.
        memories.MapGet("/stats", async (
            [FromServices] IMemoryService service, string? userId, CancellationToken ct) =>
        {
            string user = UserOr(userId);
            IReadOnlyList<MemoryRecord> active = await ListAllFilteredAsync(
                service, user, kind: null, MemoryStatuses.Active, ct).ConfigureAwait(false);
            int staged = (await ListAllFilteredAsync(
                service, user, null, MemoryStatuses.Staged, ct).ConfigureAwait(false)).Count;
            int archived = (await ListAllFilteredAsync(
                service, user, null, MemoryStatuses.Archived, ct).ConfigureAwait(false)).Count;

            // Zero-fill every kind so the client never has to tell "zero" from "field missing".
            var byKind = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string kind in MemoryKinds.All)
            {
                byKind[kind] = 0;
            }

            foreach (MemoryRecord record in active)
            {
                string? kind = MemoryMetadata.From(record)?.Kind;
                if (kind is not null && byKind.TryGetValue(kind, out int count))
                {
                    byKind[kind] = count + 1;
                }
            }

            return Results.Json(new MemoryStatsDto(byKind, staged, archived, active.Count), ResponseJson);
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

/// <summary>Memory composition counts (spec 005 R1). <c>active</c> is keyed by the five v2-001
/// kinds with every kind present (zero counts included); <c>staged</c>/<c>archived</c> are totals;
/// <c>total_active</c> is the full active count (which may exceed the sum of kinds if a row carries
/// an unrecognised kind).</summary>
public sealed record MemoryStatsDto(
    [property: JsonPropertyName("active")] IReadOnlyDictionary<string, int> Active,
    [property: JsonPropertyName("staged")] int Staged,
    [property: JsonPropertyName("archived")] int Archived,
    [property: JsonPropertyName("total_active")] int TotalActive);

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
