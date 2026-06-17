using System.CommandLine;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary><c>lore status</c> — component health and versions, a thin shim over
/// <c>GET /system/status</c>. Human mode prints a small aligned table (agent · memoryd · provider);
/// <c>--json</c> forwards the API's <c>{ version, api_version, components }</c>. If the agent is
/// down the shared scaffold reports the friendly "Lore isn't running" message and exits 3.</summary>
internal static class StatusCommand
{
    public static Command Build()
    {
        var command = new Command("status", "Show whether Lore and its components are running.");
        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult, (client, output, token) => RunAsync(client, output, token), ct));
        return command;
    }

    public static async Task<int> RunAsync(ApiClient client, Output output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        ApiResponse response = await client
            .SendAsync(HttpMethod.Get, "/system/status", cancellationToken: cancellationToken).ConfigureAwait(false);
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

        JsonNode? components = data["components"];
        output.WriteLine("Lore status");
        output.WriteLine($"  agent:    {Str(components?["agent"]) ?? "unknown"}");
        output.WriteLine($"  memoryd:  {Str(components?["memoryd"]) ?? "unknown"}");
        output.WriteLine($"  provider: {Str(components?["provider"]) ?? "unknown"}");

        string version = Str(data["version"]) ?? "unknown";
        string apiVersion = Str(data["api_version"]) ?? "unknown";
        output.WriteLine($"  version {version} (API {apiVersion})");
        return ExitCodes.Success;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? result) ? result : null;
}
