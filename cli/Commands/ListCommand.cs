using System.CommandLine;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary><c>lore list [--limit N] [--offset M]</c> — browse the full memory set, page by page, a
/// thin shim over <c>GET /memories?limit=&amp;offset=</c>. Forwards the API's paged
/// <c>{ items, total, limit, offset }</c> body as <c>--json</c> data; in human mode it shows the
/// window and the total so a user knows whether to page further.</summary>
internal static class ListCommand
{
    private static readonly Option<int> LimitOption = new("--limit")
    {
        Description = "Page size (default 50).",
        DefaultValueFactory = _ => 50,
    };

    private static readonly Option<int> OffsetOption = new("--offset")
    {
        Description = "Number of memories to skip (default 0).",
        DefaultValueFactory = _ => 0,
    };

    public static Command Build()
    {
        var command = new Command("list", "List your memories, with paging.")
        {
            LimitOption,
            OffsetOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(
                client, output, parseResult.GetValue(LimitOption), parseResult.GetValue(OffsetOption), token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, int limit, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string path = string.Create(CultureInfo.InvariantCulture, $"/memories?limit={limit}&offset={offset}");
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
        int total = (data["total"] as JsonValue)?.GetValue<int>() ?? count;
        MemoryFormatting.RenderList(
            output,
            items,
            $"Memories {offset + 1}-{offset + count} of {total}:",
            "No memories yet.");
        return ExitCodes.Success;
    }
}
