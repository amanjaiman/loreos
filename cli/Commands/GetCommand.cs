using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary><c>lore get &lt;id&gt;</c> — fetch one memory by id, a thin shim over
/// <c>GET /memories/{id}</c>. Forwards the memory object as <c>--json</c> data; a missing id maps to
/// the documented not-found exit code (via <see cref="CommandSupport.Fail"/>).</summary>
internal static class GetCommand
{
    private static readonly Argument<string> IdArgument = new("id")
    {
        Description = "The id of the memory to fetch.",
    };

    public static Command Build()
    {
        var command = new Command("get", "Fetch a single memory by id.")
        {
            IdArgument,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(client, output, parseResult.GetValue(IdArgument)!, token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string path = $"/memories/{Uri.EscapeDataString(id)}";
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

        MemoryFormatting.RenderMemory(output, data);
        return ExitCodes.Success;
    }
}
