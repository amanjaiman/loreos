using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Inference;
using Lore.Agent.Memory;
using ModelContextProtocol.Server;

namespace Lore.Agent.Mcp;

/// <summary>The MCP tool surface (spec 006): the seven core tools every MCP client — Claude
/// Desktop, Claude Code, Cursor, generic HTTP clients — sees. This is a <b>thin shim</b>
/// (constitution §8): each tool is a translation onto <see cref="IMemoryService"/> (the one
/// memory seam, §3.2) with no business logic of its own. One implementation backs both
/// transports (stdio in T002, Streamable HTTP in T003).
///
/// <para>Tool names are a <b>stable compatibility contract</b> (acceptance criterion 4): they
/// match Lore v1 so an existing <c>claude_desktop_config.json</c> connects unedited. The
/// <c>[Description]</c> strings are model-facing and drive tool selection — they are carried
/// over from v1's well-tuned wording and changed only deliberately. The methods are
/// <c>static</c> and take their service dependencies as parameters so the SDK injects them from
/// DI per call and unit tests can drive the mappings directly.</para></summary>
[McpServerToolType]
public static class LoreTools
{
    /// <summary>The default (and today, only) memory owner. Lore is single-user, so
    /// <c>user_id</c> is not part of the model-facing surface — it is carried internally so a
    /// future multi-user surface needs no breaking change (mirrors the REST contract).</summary>
    internal const string UserId = "default";

    /// <summary>The metadata key memories are grouped and tagged by. Capture writes the model's
    /// classification here (e.g. <c>coding</c>, <c>reading</c>); <see cref="AddContext"/> writes
    /// the caller's <c>category</c> to the same key so ambient and explicit memories group
    /// together in <see cref="GetProfile"/>.</summary>
    internal const string CategoryKey = "category";

    /// <summary>The category used when none is supplied or recorded.</summary>
    internal const string DefaultCategory = "general";

    [McpServerTool(Name = "get_context")]
    [Description(
        "Search the user's personal memory for context relevant to a query. Call this whenever "
        + "you need to know what the user has been doing, prefers, or has told you before — for "
        + "example at the start of a task, or when a request refers to something you have no "
        + "context on. Returns the most relevant memories, most relevant first.")]
    public static async Task<ContextResult> GetContext(
        IMemoryService memory,
        [Description("What to look for, in natural language (e.g. 'database the user is using').")]
        string query,
        [Description("Maximum number of memories to return. Defaults to 10.")]
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        IReadOnlyList<MemoryRecord> hits = await memory
            .SearchAsync(query, UserId, NormalizeLimit(limit, 10), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new ContextResult(hits.Select(MemoryHit.From).ToArray());
    }

    [McpServerTool(Name = "get_recent")]
    [Description(
        "Get the user's most recent memories, without a search query. Use this to catch up on "
        + "what the user has been doing lately when no specific topic is in question.")]
    public static async Task<ContextResult> GetRecent(
        IMemoryService memory,
        [Description("Maximum number of memories to return. Defaults to 20.")]
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        IReadOnlyList<MemoryRecord> recent = await memory
            .GetRecentAsync(UserId, NormalizeLimit(limit, 20), cancellationToken)
            .ConfigureAwait(false);
        return new ContextResult(recent.Select(MemoryHit.From).ToArray());
    }

    [McpServerTool(Name = "get_profile")]
    [Description(
        "Get the user's full memory profile, organized by category (such as coding, reading, or "
        + "preferences). Use this for a structured overview of everything Lore knows about the "
        + "user. For a narrative summary instead, use summarize_profile.")]
    public static async Task<ProfileResult> GetProfile(
        IMemoryService memory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        IReadOnlyList<MemoryRecord> all = await memory
            .GetAllAsync(UserId, cancellationToken).ConfigureAwait(false);

        ProfileCategory[] categories = all
            .GroupBy(CategoryOf)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProfileCategory(
                group.Key, group.Select(MemoryHit.From).ToArray()))
            .ToArray();
        return new ProfileResult(all.Count, categories);
    }

    [McpServerTool(Name = "summarize_profile")]
    [Description(
        "Summarize everything Lore knows about the user as a short natural-language paragraph. "
        + "Use this when you want a quick, readable sense of who the user is and what they care "
        + "about, rather than a categorized list (get_profile) or a targeted search (get_context).")]
    public static async Task<string> SummarizeProfile(
        IMemoryService memory,
        IInferenceBackend inference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(inference);

        IReadOnlyList<MemoryRecord> all = await memory
            .GetAllAsync(UserId, cancellationToken).ConfigureAwait(false);
        if (all.Count == 0)
        {
            return "Lore has no memories about the user yet.";
        }

        var builder = new StringBuilder();
        foreach (MemoryRecord record in all)
        {
            builder.Append("- ").AppendLine(record.Memory);
        }

        var request = new InferenceRequest(
            SystemPrompt:
                "You are summarizing a user's personal memory profile. Given a list of facts Lore "
                + "has recorded about the user, write a short, friendly paragraph (a few sentences) "
                + "describing who they are and what they are working on. Use only the facts given; "
                + "do not invent details.",
            UserPrompt: builder.ToString(),
            Temperature: 0.3);

        string? summary = await inference.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        // No provider configured (or the model returned nothing): fall back to the raw facts so
        // the tool is still useful rather than empty.
        return string.IsNullOrWhiteSpace(summary)
            ? "Summary unavailable (no model configured). Known facts:\n" + builder.ToString().TrimEnd()
            : summary.Trim();
    }

    [McpServerTool(Name = "add_context")]
    [Description(
        "Record something new about the user in their personal memory. Use this when the user "
        + "tells you a durable fact, preference, or decision worth remembering for later — not "
        + "for transient chat. Lore extracts and de-duplicates the text, so phrase it as a plain "
        + "statement (e.g. 'The user prefers TypeScript over JavaScript').")]
    public static async Task<AddResult> AddContext(
        IMemoryService memory,
        [Description("The fact to remember, as a plain natural-language statement.")]
        string text,
        [Description("Optional category to file it under (e.g. 'preferences', 'coding'). Defaults to 'general'.")]
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        string resolved = string.IsNullOrWhiteSpace(category) ? DefaultCategory : category.Trim();
        var metadata = new Dictionary<string, object?> { [CategoryKey] = resolved };
        IReadOnlyList<AddedMemory> added = await memory
            .RememberAsync(text, UserId, metadata, cancellationToken).ConfigureAwait(false);
        return new AddResult(added.Select(AddedMemoryDto.From).ToArray());
    }

    [McpServerTool(Name = "update_context")]
    [Description(
        "Update what Lore remembers about a topic. Finds the memory most relevant to the topic "
        + "and replaces it with the new information. Use this when a previously recorded fact has "
        + "changed (e.g. the user switched editors). If nothing matches the topic, the new "
        + "information is recorded as a fresh memory so it is not lost.")]
    public static async Task<UpdateResult> UpdateContext(
        IMemoryService memory,
        [Description("The topic whose memory should change (used to find the existing memory).")]
        string topic,
        [Description("The new information to store in its place.")]
        string newInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        IReadOnlyList<MemoryRecord> matches = await memory
            .SearchAsync(topic, UserId, 1, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (matches.Count == 0)
        {
            // No existing memory on this topic: add the new info rather than silently dropping it
            // (an upsert — recorded as a deliberate decision in plan.md).
            IReadOnlyList<AddedMemory> added = await memory
                .RememberAsync(newInfo, UserId, null, cancellationToken).ConfigureAwait(false);
            string? newId = added.Count > 0 ? added[0].Id : null;
            return new UpdateResult("added", newId, newInfo, null);
        }

        MemoryRecord target = matches[0];
        MemoryRecord? updated = await memory
            .UpdateAsync(target.Id, newInfo, cancellationToken: cancellationToken).ConfigureAwait(false);
        return updated is null
            ? new UpdateResult("not_found", target.Id, null, target.Memory)
            : new UpdateResult("updated", updated.Id, updated.Memory, target.Memory);
    }

    [McpServerTool(Name = "forget")]
    [Description(
        "Forget what Lore remembers about a topic. Finds the memory most relevant to the topic "
        + "and deletes it. Use this when the user asks you to forget something. Only the single "
        + "best match is removed; if more memories relate to the topic, call again to remove them.")]
    public static async Task<ForgetResult> Forget(
        IMemoryService memory,
        [Description("The topic to forget (used to find the memory to delete).")]
        string topic,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        IReadOnlyList<MemoryRecord> matches = await memory
            .SearchAsync(topic, UserId, 5, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            return new ForgetResult(false, null, null, 0);
        }

        MemoryRecord target = matches[0];
        bool deleted = await memory.DeleteAsync(target.Id, cancellationToken).ConfigureAwait(false);
        int remaining = deleted ? matches.Count - 1 : matches.Count;
        return new ForgetResult(deleted, deleted ? target.Id : null, deleted ? target.Memory : null, remaining);
    }

    private static string CategoryOf(MemoryRecord record)
    {
        if (record.Metadata is not null
            && record.Metadata.TryGetValue(CategoryKey, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            string category = value.GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(category))
            {
                return category;
            }
        }

        return DefaultCategory;
    }

    // Absent/non-positive falls back; oversized is clamped so a model can't ask the store for an
    // unbounded page (mirrors the REST layer's NormalizeLimit).
    private static int NormalizeLimit(int limit, int fallback) =>
        limit <= 0 ? fallback : Math.Min(limit, 1000);
}

/// <summary>A single memory as returned to an MCP client.</summary>
public sealed record MemoryHit(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("score")] double? Score,
    [property: JsonPropertyName("category")] string? Category)
{
    public static MemoryHit From(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        string? category = null;
        if (record.Metadata is not null
            && record.Metadata.TryGetValue(LoreTools.CategoryKey, out JsonElement value)
            && value.ValueKind == JsonValueKind.String)
        {
            category = value.GetString();
        }

        return new MemoryHit(record.Id, record.Memory, record.Score, category);
    }
}

/// <summary>The result of <c>get_context</c> / <c>get_recent</c>: matching memories.</summary>
public sealed record ContextResult(
    [property: JsonPropertyName("memories")] IReadOnlyList<MemoryHit> Memories);

/// <summary>One category group in <c>get_profile</c>.</summary>
public sealed record ProfileCategory(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("memories")] IReadOnlyList<MemoryHit> Memories);

/// <summary>The result of <c>get_profile</c>: total count plus per-category groups.</summary>
public sealed record ProfileResult(
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("categories")] IReadOnlyList<ProfileCategory> Categories);

/// <summary>One memory produced by <c>add_context</c> and what happened to it
/// (<c>ADD</c>, <c>UPDATE</c>, or <c>NONE</c>, from the memory engine).</summary>
public sealed record AddedMemoryDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("event")] string Event)
{
    public static AddedMemoryDto From(AddedMemory added)
    {
        ArgumentNullException.ThrowIfNull(added);
        return new AddedMemoryDto(added.Id, added.Memory, added.Event);
    }
}

/// <summary>The result of <c>add_context</c>: the memories the engine distilled from the text.</summary>
public sealed record AddResult(
    [property: JsonPropertyName("added")] IReadOnlyList<AddedMemoryDto> Added);

/// <summary>The result of <c>update_context</c>. <c>outcome</c> is <c>updated</c> (an existing
/// memory was replaced), <c>added</c> (no match, so the info was stored fresh), or
/// <c>not_found</c> (a match existed but vanished before the update).</summary>
public sealed record UpdateResult(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("previous_text")] string? PreviousText);

/// <summary>The result of <c>forget</c>. <c>forgotten</c> is whether a memory was deleted;
/// <c>remaining_matches</c> is how many other memories still match the topic.</summary>
public sealed record ForgetResult(
    [property: JsonPropertyName("forgotten")] bool Forgotten,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("remaining_matches")] int RemainingMatches);
