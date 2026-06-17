using System.CommandLine;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands;

/// <summary>Shared scaffolding every command runs through, so the cross-cutting concerns live in
/// one place: build the <see cref="Output"/>, resolve <c>--api-url</c>, construct the
/// <see cref="ApiClient"/>, and turn failures into the documented exit codes — the agent-down path
/// (<see cref="ExitCodes.AgentUnreachable"/>, a friendly message, never a stack trace) and the
/// HTTP-status mapping (404 → not-found, 400 → bad-usage, else runtime). Command <em>logic</em>
/// lives in each command's <c>RunAsync</c>, which takes its dependencies as parameters and is
/// tested directly.</summary>
internal static class CommandSupport
{
    /// <summary>Run a command body with a resolved client and output, translating an unreachable
    /// agent into the friendly exit-3 path.</summary>
    public static async Task<int> RunAsync(
        ParseResult parseResult,
        Func<ApiClient, Output, CancellationToken, Task<int>> body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(body);

        Output output = CliRoot.CreateOutput(parseResult);
        if (!CliRoot.TryResolveApiUrl(parseResult, output, out Uri apiUrl))
        {
            return ExitCodes.BadUsage;
        }

        using var client = new ApiClient(apiUrl);
        try
        {
            return await body(client, output, cancellationToken).ConfigureAwait(false);
        }
        catch (AgentUnreachableException ex)
        {
            output.WriteError("agent_unreachable", AgentDownMessage(ex.BaseUrl));
            return ExitCodes.AgentUnreachable;
        }
    }

    /// <summary>The actionable "Lore isn't running" message (acceptance criterion 6).</summary>
    public static string AgentDownMessage(Uri? baseUrl)
    {
        string target = baseUrl is null ? ApiClient.DefaultBaseUrl.ToString() : baseUrl.ToString();
        return $"Lore isn't running — couldn't reach the local API at {target}. "
            + "Start the Lore app (or the agent) and try again.";
    }

    /// <summary>Map a non-success API response onto an exit code, writing the error through
    /// <paramref name="output"/>. The message prefers the API's own <c>{ "error": ... }</c> body.</summary>
    public static int Fail(Output output, ApiResponse response)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(response);

        string message = ExtractError(response)
            ?? $"the Lore API returned HTTP {(int)response.StatusCode}.";

        (string code, int exit) = response.StatusCode switch
        {
            HttpStatusCode.NotFound => ("not_found", ExitCodes.NotFound),
            HttpStatusCode.BadRequest => ("bad_usage", ExitCodes.BadUsage),
            _ => ("runtime_error", ExitCodes.RuntimeFailure),
        };

        output.WriteError(code, message);
        return exit;
    }

    private static string? ExtractError(ApiResponse response)
    {
        JsonNode? error = response.ReadJson()?["error"];
        return error is JsonValue value && value.TryGetValue(out string? message) ? message : null;
    }
}
