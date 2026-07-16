using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lore.Cli.Commands;

/// <summary><c>lore recall "&lt;message&gt;" [--k N] [--kinds a,b]</c> — the every-turn memory
/// check (v2-001), a thin shim over <c>POST /recall</c>. Built for scripting agents:
/// <c>--json</c> forwards the API's <c>{ results }</c> verbatim, and an empty result is a
/// success (exit 0) — "nothing relevant" is the common, correct answer.</summary>
internal static class RecallCommand
{
    private static readonly Argument<string> QueryArgument = new("query")
    {
        Description = "The user's message (or its substance) to recall against.",
    };

    private static readonly Option<int> KOption = new("--k")
    {
        Description = "Maximum number of memories to return (default 5).",
        DefaultValueFactory = _ => 5,
    };

    private static readonly Option<string?> KindsOption = new("--kinds")
    {
        Description = "Comma-separated kind filter (identity,preference,state,experience,project).",
    };

    public static Command Build()
    {
        var command = new Command(
            "recall", "Recall durable memories relevant to a message (the ambient memory check).")
        {
            QueryArgument,
            KOption,
            KindsOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(
                client,
                output,
                parseResult.GetValue(QueryArgument)!,
                parseResult.GetValue(KOption),
                parseResult.GetValue(KindsOption),
                token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string query, int k, string? kinds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string[]? kindList = kinds?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ApiResponse response = await client
            .SendAsync(HttpMethod.Post, "/recall", new RecallBody(query, k, kindList), cancellationToken)
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
        if (results is null || results.Count == 0)
        {
            output.WriteLine($"Nothing in memory is relevant to \"{query}\".");
            return ExitCodes.Success;
        }

        output.WriteLine($"{results.Count} relevant memor{(results.Count == 1 ? "y" : "ies")}:");
        foreach (JsonNode? node in results)
        {
            string statement = node?["statement"]?.GetValue<string>() ?? string.Empty;
            string kind = node?["kind"]?.GetValue<string>() ?? string.Empty;
            double score = node?["score"]?.GetValue<double>() ?? 0;
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"  [{kind}] {statement} ({score:0.00})"));
        }

        return ExitCodes.Success;
    }

    /// <summary>The <c>POST /recall</c> request body.</summary>
    private sealed record RecallBody(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("k")] int K,
        [property: JsonPropertyName("kinds")] string[]? Kinds);
}
