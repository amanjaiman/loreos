using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lore.Cli.Commands;

/// <summary><c>lore search "&lt;query&gt;" [--limit N]</c> — semantic search over the user's memory,
/// a thin shim over <c>POST /memories/search</c>. In <c>--json</c> mode the API's
/// <c>{ results }</c> body is forwarded verbatim as the envelope data; in human mode the hits are
/// rendered most-relevant-first with their scores.</summary>
internal static class SearchCommand
{
    private static readonly Argument<string> QueryArgument = new("query")
    {
        Description = "What to look for, in natural language.",
    };

    private static readonly Option<int> LimitOption = new("--limit")
    {
        Description = "Maximum number of results to return (default 10).",
        DefaultValueFactory = _ => 10,
    };

    public static Command Build()
    {
        var command = new Command("search", "Search your memory for context relevant to a query.")
        {
            QueryArgument,
            LimitOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(
                client, output, parseResult.GetValue(QueryArgument)!, parseResult.GetValue(LimitOption), token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string query, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        ApiResponse response = await client
            .SendAsync(HttpMethod.Post, "/memories/search", new SearchBody(query, limit), cancellationToken)
            .ConfigureAwait(false);
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
        int count = results?.Count ?? 0;
        MemoryFormatting.RenderList(
            output,
            results,
            $"{count} result{(count == 1 ? string.Empty : "s")} for \"{query}\":",
            $"No memories match \"{query}\".");
        return ExitCodes.Success;
    }

    /// <summary>The <c>POST /memories/search</c> request body.</summary>
    private sealed record SearchBody(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("limit")] int Limit);
}
