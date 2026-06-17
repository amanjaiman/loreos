using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary><c>lore recent [--limit N]</c> — the user's most recent memories (mirrors the MCP
/// <c>get_recent</c> intent), a thin shim over <c>GET /memories?limit=N</c>. Forwards the API's
/// paged <c>{ items, total, limit, offset }</c> body as <c>--json</c> data; renders the items in
/// human mode. (Recency follows the store's order; see plan.md.)</summary>
internal static class RecentCommand
{
    private static readonly Option<int> LimitOption = new("--limit")
    {
        Description = "Maximum number of memories to return (default 20).",
        DefaultValueFactory = _ => 20,
    };

    public static Command Build()
    {
        var command = new Command("recent", "Show your most recent memories.")
        {
            LimitOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(client, output, parseResult.GetValue(LimitOption), token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string path = string.Create(CultureInfo.InvariantCulture, $"/memories?limit={limit}");
        ApiResponse response = await client
            .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);
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

        JsonArray? items = data["items"] as JsonArray;
        int count = items?.Count ?? 0;
        MemoryFormatting.RenderList(
            output,
            items,
            $"{count} recent memor{(count == 1 ? "y" : "ies")}:",
            "No memories yet.");
        return ExitCodes.Success;
    }
}
