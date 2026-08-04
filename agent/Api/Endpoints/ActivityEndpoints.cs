using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The v2-001 app-facing explainability + curation surface: episodes and the
/// decision trail (the Activity feed), the staging actions (promote/dismiss — the user
/// is the authority, so no budget applies), the still-true confirmation, and the day's
/// capture economy. Every write is an <see cref="IMemoryService"/> metadata patch plus a
/// decision row — no business logic beyond translation (constitution §3.1).</summary>
public static class ActivityEndpoints
{
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/episodes", async (
            [FromServices] ActivityStore activity, int? limit, CancellationToken ct) =>
        {
            IReadOnlyList<Episode> episodes = await activity
                .GetRecentEpisodesAsync(Normalize(limit, 50), ct).ConfigureAwait(false);
            return Results.Json(new EpisodesDto(episodes.Select(EpisodeDto.From).ToArray()), ResponseJson);
        });

        app.MapGet("/episodes/{id}", async (
            string id, [FromServices] ActivityStore activity, CancellationToken ct) =>
        {
            Episode? episode = await activity.GetEpisodeAsync(id, ct).ConfigureAwait(false);
            return episode is null
                ? Results.Json(
                    new ErrorResponse($"no episode with id '{id}'"), ResponseJson,
                    statusCode: StatusCodes.Status404NotFound)
                : Results.Json(EpisodeDto.From(episode), ResponseJson);
        });

        app.MapGet("/decisions", async (
            [FromServices] ActivityStore activity, int? limit, CancellationToken ct) =>
        {
            IReadOnlyList<DecisionEntry> decisions = await activity
                .GetRecentDecisionsAsync(Normalize(limit, 100), ct).ConfigureAwait(false);
            return Results.Json(
                new DecisionsDto(decisions.Select(DecisionDto.From).ToArray()), ResponseJson);
        });

        // The day's capture economy: decision counts since UTC midnight + the budget.
        app.MapGet("/system/economy", async (
            [FromServices] ActivityStore activity,
            [FromServices] LifecycleOptions lifecycle,
            [FromServices] TimeProvider time,
            CancellationToken ct) =>
        {
            var midnight = new DateTimeOffset(time.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            IReadOnlyDictionary<string, int> counts = await activity
                .GetDecisionCountsSinceAsync(midnight, ct).ConfigureAwait(false);
            int promoted = counts.GetValueOrDefault("promoted");
            return Results.Json(
                new EconomyDto(counts, promoted, lifecycle.DailyBudget), ResponseJson);
        });

        // Evidence for a memory (spec 005 R3): the episodes that supported it, resolved in one
        // call instead of the client fanning out over GET /episodes/{id}. The episode titles are
        // already filter-cleared — episode intake only ever sees observations that passed the whole
        // sensitivity chain (a blocked window never becomes an observation), so the same redaction
        // guarantee as R2 holds without re-screening here.
        app.MapGet("/memories/{id}/evidence", async (
            string id, [FromServices] IMemoryService memory, [FromServices] ActivityStore activity,
            CancellationToken ct) =>
        {
            MemoryRecord? record = await memory.GetAsync(id, ct).ConfigureAwait(false);
            if (record is null)
            {
                return NotFound(id);
            }

            IReadOnlyList<string> episodeIds = MemoryMetadata.From(record)?.Episodes ?? [];
            var episodes = new List<EvidenceEpisodeDto>();
            foreach (string episodeId in episodeIds)
            {
                if (string.IsNullOrEmpty(episodeId))
                {
                    continue;
                }

                Episode? episode = await activity.GetEpisodeAsync(episodeId, ct).ConfigureAwait(false);
                if (episode is not null)
                {
                    episodes.Add(EvidenceEpisodeDto.From(episode));
                }
            }

            return Results.Json(new EvidenceDto(episodes), ResponseJson);
        });

        // Staging curation: the user's tap outranks the budget and the evidence rule.
        app.MapPost("/staging/{id}/promote", (string id, [FromServices] IMemoryService memory,
            [FromServices] ActivityStore activity, [FromServices] LifecycleOptions lifecycle,
            [FromServices] TimeProvider time, CancellationToken ct) =>
            TransitionStagedAsync(
                id, memory, activity, time, promote: true, lifecycle, ct));

        app.MapPost("/staging/{id}/dismiss", (string id, [FromServices] IMemoryService memory,
            [FromServices] ActivityStore activity, [FromServices] LifecycleOptions lifecycle,
            [FromServices] TimeProvider time, CancellationToken ct) =>
            TransitionStagedAsync(
                id, memory, activity, time, promote: false, lifecycle, ct));

        // Still-true confirmation: for state, push the horizon out; for everything else,
        // just re-stamp. Confirmation is user authority — confidence rises to the cap.
        app.MapPost("/memories/{id}/confirm", async (
            string id, [FromServices] IMemoryService memory,
            [FromServices] LifecycleOptions lifecycle, [FromServices] TimeProvider time,
            CancellationToken ct) =>
        {
            MemoryRecord? record = await memory.GetAsync(id, ct).ConfigureAwait(false);
            MemoryMetadata? meta = record is null ? null : MemoryMetadata.From(record);
            if (record is null || meta is null)
            {
                return NotFound(id);
            }

            long now = time.GetUtcNow().ToUnixTimeSeconds();
            var patch = new Dictionary<string, object?>
            {
                ["updated_reason"] = MemoryUpdateReasons.Confirmed,
                ["confidence"] = Math.Max(meta.Confidence, 0.9),
            };
            if (meta.Kind == MemoryKinds.State)
            {
                patch["expires_at"] = now + lifecycle.DefaultHorizonDays * 86_400L;
            }

            MemoryRecord? updated = await memory.PatchMetadataAsync(id, patch, ct).ConfigureAwait(false);
            return updated is null ? NotFound(id) : Results.Json(MemoryDto.From(updated), ResponseJson);
        });

        return app;
    }

    private static async Task<IResult> TransitionStagedAsync(
        string id, IMemoryService memory, ActivityStore activity, TimeProvider time,
        bool promote, LifecycleOptions lifecycle, CancellationToken ct)
    {
        MemoryRecord? record = await memory.GetAsync(id, ct).ConfigureAwait(false);
        MemoryMetadata? meta = record is null ? null : MemoryMetadata.From(record);
        if (record is null || meta is null)
        {
            return NotFound(id);
        }

        if (meta.Status != MemoryStatuses.Staged)
        {
            return Results.Json(
                new ErrorResponse($"memory '{id}' is not staged (status: {meta.Status})"),
                ResponseJson,
                statusCode: StatusCodes.Status409Conflict);
        }

        long now = time.GetUtcNow().ToUnixTimeSeconds();
        Dictionary<string, object?> patch = promote
            ? new Dictionary<string, object?>
            {
                ["status"] = MemoryStatuses.Active,
                ["established_at"] = now,
                ["confidence"] = Math.Max(meta.Confidence, 0.9), // the user vouched for it
                ["expires_at"] = meta.Kind == MemoryKinds.State
                    ? now + lifecycle.DefaultHorizonDays * 86_400L
                    : MemoryMetadata.FarFutureUnixSeconds,
                ["updated_reason"] = MemoryUpdateReasons.UserEdit,
                ["user_edited"] = true,
            }
            : new Dictionary<string, object?>
            {
                ["status"] = MemoryStatuses.Archived,
                ["updated_reason"] = MemoryUpdateReasons.UserEdit,
                ["user_edited"] = true,
            };

        MemoryRecord? updated = await memory.PatchMetadataAsync(id, patch, ct).ConfigureAwait(false);
        if (updated is null)
        {
            return NotFound(id);
        }

        await activity.LogDecisionAsync(
            new DecisionEntry(
                time.GetUtcNow(), string.Empty,
                promote ? "user_promoted" : "user_dismissed",
                "user action from the staging review",
                updated.Memory, meta.Kind, id),
            ct).ConfigureAwait(false);
        return Results.Json(MemoryDto.From(updated), ResponseJson);
    }

    private static int Normalize(int? limit, int fallback) =>
        limit is null or <= 0 ? fallback : Math.Min(limit.Value, 1000);

    private static IResult NotFound(string id) =>
        Results.Json(
            new ErrorResponse($"no memory with id '{id}'"), ResponseJson,
            statusCode: StatusCodes.Status404NotFound);
}

/// <summary>An episode on the wire — what the distiller saw, for provenance views.</summary>
public sealed record EpisodeDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("executables")] IReadOnlyList<string> Executables,
    [property: JsonPropertyName("titles")] IReadOnlyList<string> Titles,
    [property: JsonPropertyName("samples")] IReadOnlyList<string> Samples,
    [property: JsonPropertyName("observation_count")] int ObservationCount)
{
    public static EpisodeDto From(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new EpisodeDto(
            episode.Id, episode.StartedAt, episode.EndedAt, episode.Executables,
            episode.Titles, episode.Samples, episode.ObservationCount);
    }
}

public sealed record EpisodesDto(
    [property: JsonPropertyName("items")] IReadOnlyList<EpisodeDto> Items);

/// <summary>One supporting episode on the evidence wire (spec 005 R3): the same provenance an
/// episode carries, minus the sample text — a memory's "Show evidence" needs when and where, not
/// the raw captures. <c>titles</c> are filter-cleared, same as <see cref="EpisodeDto"/>.</summary>
public sealed record EvidenceEpisodeDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("started_at")] DateTimeOffset StartedAt,
    [property: JsonPropertyName("ended_at")] DateTimeOffset EndedAt,
    [property: JsonPropertyName("executables")] IReadOnlyList<string> Executables,
    [property: JsonPropertyName("titles")] IReadOnlyList<string> Titles,
    [property: JsonPropertyName("observation_count")] int ObservationCount)
{
    public static EvidenceEpisodeDto From(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return new EvidenceEpisodeDto(
            episode.Id, episode.StartedAt, episode.EndedAt,
            episode.Executables, episode.Titles, episode.ObservationCount);
    }
}

/// <summary>The episodes that supported a memory, for its evidence view (spec 005 R3).</summary>
public sealed record EvidenceDto(
    [property: JsonPropertyName("episodes")] IReadOnlyList<EvidenceEpisodeDto> Episodes);

/// <summary>One decision-trail row on the wire — the Activity feed's unit.</summary>
public sealed record DecisionDto(
    [property: JsonPropertyName("at")] DateTimeOffset At,
    [property: JsonPropertyName("episode_id")] string EpisodeId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("statement")] string Statement,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("memory_id")] string MemoryId)
{
    public static DecisionDto From(DecisionEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new DecisionDto(
            entry.At, entry.EpisodeId, entry.Action, entry.Reason,
            entry.Statement, entry.Kind, entry.MemoryId);
    }
}

public sealed record DecisionsDto(
    [property: JsonPropertyName("items")] IReadOnlyList<DecisionDto> Items);

/// <summary>The day's capture economy since UTC midnight, plus the promotion budget.</summary>
public sealed record EconomyDto(
    [property: JsonPropertyName("decisions_today")] IReadOnlyDictionary<string, int> DecisionsToday,
    [property: JsonPropertyName("promoted_today")] int PromotedToday,
    [property: JsonPropertyName("daily_budget")] int DailyBudget);
