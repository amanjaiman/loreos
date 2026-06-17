using System.Text.Json.Serialization;
using Lore.Agent.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The local-telemetry HTTP surface (spec 005 T003): <c>GET /recent</c> (the raw text
/// Lore actually captured) and <c>GET /activity</c> (the human-readable log of what it decided
/// per window). Both read 003's <see cref="ActivityStore"/> and <b>only</b> that store — this
/// is operational data the user can inspect, deliberately isolated from
/// <c>IMemoryService</c>/mem0 (constitution: "the code is the audit trail"). Nothing here
/// touches the memory seam, so activity data is never conflated with memories.</summary>
public static class RecentEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    /// <summary>Map the recent/activity endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapRecentEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Recent raw captures: the filtered text Lore read, newest first.
        app.MapGet("/recent", async (
            [FromServices] ActivityStore store, int? limit, CancellationToken ct) =>
        {
            IReadOnlyList<RawCaptureEntry> captures = await store
                .GetRecentRawCapturesAsync(Normalize(limit), ct).ConfigureAwait(false);
            return Results.Ok(new RecentCaptures(captures.Select(RawCaptureDto.From).ToArray()));
        });

        // Activity log: one row per window decision (captured / skipped / filtered), newest first.
        app.MapGet("/activity", async (
            [FromServices] ActivityStore store, int? limit, CancellationToken ct) =>
        {
            IReadOnlyList<ActivityLogEntry> entries = await store
                .GetRecentActivityAsync(Normalize(limit), ct).ConfigureAwait(false);
            return Results.Ok(new ActivityFeed(entries.Select(ActivityLogDto.From).ToArray()));
        });

        return app;
    }

    private static int Normalize(int? limit) =>
        limit is null or <= 0 ? DefaultLimit : Math.Min(limit.Value, MaxLimit);
}

/// <summary>One activity-log row on the wire: what Lore did with a window and why.</summary>
public sealed record ActivityLogDto(
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("executable")] string Executable,
    [property: JsonPropertyName("window_title")] string WindowTitle,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("observation")] string Observation,
    [property: JsonPropertyName("category")] string Category)
{
    public static ActivityLogDto From(ActivityLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new ActivityLogDto(
            entry.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            entry.Executable,
            entry.WindowTitle,
            entry.Decision.ToString(),
            entry.Reason,
            entry.Observation,
            entry.Category);
    }
}

/// <summary>One raw-capture row on the wire: the filtered text Lore read and how it read it.</summary>
public sealed record RawCaptureDto(
    [property: JsonPropertyName("at")] string At,
    [property: JsonPropertyName("executable")] string Executable,
    [property: JsonPropertyName("window_title")] string WindowTitle,
    [property: JsonPropertyName("extraction_source")] string ExtractionSource,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("text")] string Text)
{
    public static RawCaptureDto From(RawCaptureEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new RawCaptureDto(
            entry.At.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            entry.Executable,
            entry.WindowTitle,
            entry.ExtractionSource,
            entry.ContentType,
            entry.Text);
    }
}

/// <summary>The recent raw captures, newest first.</summary>
public sealed record RecentCaptures(
    [property: JsonPropertyName("items")] IReadOnlyList<RawCaptureDto> Items);

/// <summary>The recent activity-log entries, newest first.</summary>
public sealed record ActivityFeed(
    [property: JsonPropertyName("items")] IReadOnlyList<ActivityLogDto> Items);
