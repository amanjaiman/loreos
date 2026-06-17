using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lore.Cli.Commands;

/// <summary><c>lore add "&lt;text&gt;" [--category C]</c> — remember an observation, a thin shim over
/// <c>POST /memories</c>. memoryd extracts and de-dupes, so one add may yield zero, one, or several
/// memories; the <c>--json</c> data forwards the API's <c>{ results }</c> (each with its
/// <c>event</c>: ADD/UPDATE/NONE). The category is filed under <c>metadata.category</c> and defaults
/// to <c>general</c>, matching the MCP <c>add_context</c> tool so explicit and ambient memories
/// group together.</summary>
internal static class AddCommand
{
    /// <summary>The category used when <c>--category</c> is omitted (mirrors MCP add_context).</summary>
    private const string DefaultCategory = "general";

    private static readonly Argument<string> TextArgument = new("text")
    {
        Description = "The fact to remember, as a plain natural-language statement.",
    };

    private static readonly Option<string> CategoryOption = new("--category")
    {
        Description = "Category to file it under (e.g. 'preferences', 'coding'). Defaults to 'general'.",
        DefaultValueFactory = _ => DefaultCategory,
    };

    public static Command Build()
    {
        var command = new Command("add", "Remember a new fact about yourself.")
        {
            TextArgument,
            CategoryOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(
                client, output, parseResult.GetValue(TextArgument)!, parseResult.GetValue(CategoryOption)!, token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string text, string category, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string resolvedCategory = string.IsNullOrWhiteSpace(category) ? DefaultCategory : category.Trim();
        var body = new AddBody(text, new Dictionary<string, string> { ["category"] = resolvedCategory });

        ApiResponse response = await client
            .SendAsync(HttpMethod.Post, "/memories", body, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CommandSupport.Fail(output, response);
        }

        JsonNode data = response.ReadJson() ?? new JsonObject();
        if (output.IsJson)
        {
            output.WriteData(data);
            return ExitCodes.Success;
        }

        JsonArray? results = data["results"] as JsonArray;
        if (results is null || results.Count == 0)
        {
            output.WriteLine("Nothing new to remember — Lore already knew this.");
            return ExitCodes.Success;
        }

        output.WriteLine("Remembered:");
        foreach (JsonNode? result in results)
        {
            if (result is null)
            {
                continue;
            }

            string memory = (result["memory"] as JsonValue)?.GetValue<string>() ?? "(empty)";
            string evt = (result["event"] as JsonValue)?.GetValue<string>() ?? "ADD";
            output.WriteLine($"- {memory} ({Describe(evt)})");
        }

        return ExitCodes.Success;
    }

    // Map memoryd's event verbs onto plain words for human output.
    private static string Describe(string evt) => evt.ToUpperInvariant() switch
    {
        "ADD" => "added",
        "UPDATE" => "updated",
        "DELETE" => "removed",
        _ => "no change",
    };

    /// <summary>The <c>POST /memories</c> request body.</summary>
    private sealed record AddBody(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string> Metadata);
}
