using System.Globalization;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary>Human-mode rendering for memories returned by the read commands. Pure formatting over
/// the API's JSON shape (constitution §8: the CLI translates, it doesn't re-model) — one place so
/// <c>search</c>, <c>recent</c>, <c>list</c>, and <c>get</c> present a memory the same way.</summary>
internal static class MemoryFormatting
{
    /// <summary>Render a flat array of memories under a header, or an empty-state message.</summary>
    public static void RenderList(Output output, JsonArray? memories, string header, string emptyMessage)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (memories is null || memories.Count == 0)
        {
            output.WriteLine(emptyMessage);
            return;
        }

        output.WriteLine(header);
        output.WriteLine();
        foreach (JsonNode? memory in memories)
        {
            RenderMemory(output, memory);
        }
    }

    /// <summary>Render a single memory: its text on one line, then a dim detail line with whatever
    /// of id · category · score · timestamp is present.</summary>
    public static void RenderMemory(Output output, JsonNode? memory)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (memory is null)
        {
            return;
        }

        string text = Str(memory["memory"]) ?? "(empty)";
        output.WriteLine($"- {text}");

        var details = new List<string>();
        if (Str(memory["id"]) is { Length: > 0 } id)
        {
            details.Add($"id {id}");
        }

        if (Category(memory) is { Length: > 0 } category)
        {
            details.Add(category);
        }

        if (Num(memory["score"]) is double score)
        {
            details.Add($"score {score.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (Str(memory["created_at"]) is { Length: > 0 } created)
        {
            details.Add(created);
        }

        if (details.Count > 0)
        {
            output.WriteLine($"  {string.Join(" · ", details)}");
        }
    }

    private static string? Category(JsonNode memory)
    {
        JsonNode? metadata = memory["metadata"];
        return metadata is null ? null : Str(metadata["category"]);
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? result) ? result : null;

    private static double? Num(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out double result) ? result : null;
}
