using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Lore.Cli.Commands;

/// <summary><c>lore forget "&lt;topic&gt;"</c> — forget what Lore remembers about a topic. The local
/// API has no forget-by-topic endpoint, only <c>DELETE /memories/{id}</c>, so this command makes the
/// same two API calls the MCP <c>forget</c> tool makes against the memory seam: search the topic,
/// then delete the single best match. That is orchestration over the API, not new behavior
/// (constitution §8) — and deleting only the best match keeps a careless <c>forget</c> from wiping a
/// whole topic at once (re-run to remove more). Matching nothing is a successful no-op, reported via
/// <c>forgotten: false</c> so a script can branch without treating it as an error.</summary>
internal static class ForgetCommand
{
    // How many matches to fetch: enough to report how many remain after removing the best one.
    private const int SearchDepth = 5;

    private static readonly Argument<string> TopicArgument = new("topic")
    {
        Description = "The topic to forget (used to find the memory to delete).",
    };

    public static Command Build()
    {
        var command = new Command("forget", "Forget what Lore remembers about a topic.")
        {
            TopicArgument,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(client, output, parseResult.GetValue(TopicArgument)!, token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string topic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        // 1. Find the memories most relevant to the topic.
        ApiResponse search = await client
            .SendAsync(HttpMethod.Post, "/memories/search", new SearchBody(topic, SearchDepth), cancellationToken)
            .ConfigureAwait(false);
        if (!search.IsSuccess)
        {
            return CommandSupport.Fail(output, search);
        }

        JsonArray? matches = search.ReadJson()?["results"] as JsonArray;
        if (matches is null || matches.Count == 0)
        {
            return ReportNothingMatched(output, topic);
        }

        JsonNode best = matches[0]!;
        string id = (best["id"] as JsonValue)?.GetValue<string>() ?? string.Empty;
        string memory = (best["memory"] as JsonValue)?.GetValue<string>() ?? string.Empty;

        // 2. Delete the single best match.
        ApiResponse delete = await client
            .SendAsync(HttpMethod.Delete, $"/memories/{Uri.EscapeDataString(id)}", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!delete.IsSuccess)
        {
            // A 404 here means the memory vanished between search and delete; surface it honestly.
            return CommandSupport.Fail(output, delete);
        }

        int remaining = matches.Count - 1;
        if (output.IsJson)
        {
            output.WriteData(new ForgetResultData(true, id, memory, remaining));
            return ExitCodes.Success;
        }

        output.WriteLine($"Forgot: {memory}");
        if (remaining > 0)
        {
            output.WriteLine($"  {remaining} other memor{(remaining == 1 ? "y" : "ies")} still match \"{topic}\" — run again to remove more.");
        }

        return ExitCodes.Success;
    }

    private static int ReportNothingMatched(Output output, string topic)
    {
        if (output.IsJson)
        {
            output.WriteData(new ForgetResultData(false, null, null, 0));
        }
        else
        {
            output.WriteLine($"Nothing matched \"{topic}\" — nothing to forget.");
        }

        return ExitCodes.Success;
    }

    /// <summary>The <c>POST /memories/search</c> request body used to locate the topic.</summary>
    private sealed record SearchBody(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("limit")] int Limit);

    /// <summary>The <c>--json</c> result of a forget: whether anything was deleted, what it was, and
    /// how many other memories still match the topic.</summary>
    private sealed record ForgetResultData(
        [property: JsonPropertyName("forgotten")] bool Forgotten,
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("memory")] string? Memory,
        [property: JsonPropertyName("remaining_matches")] int RemainingMatches);
}
