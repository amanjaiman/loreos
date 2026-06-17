using System.CommandLine;

namespace Lore.Cli;

/// <summary>Assembles the <c>lore</c> command tree: the root command, the two global options every
/// command shares (<c>--json</c>, <c>--api-url</c>), and — as later tasks land — the subcommands.
/// Kept separate from <see cref="Program"/> so tests can build the exact same surface in-process
/// and invoke it without spawning a process.</summary>
internal static class CliRoot
{
    /// <summary>Global <c>--json</c>: switch every command from human text to the versioned
    /// machine envelope (<see cref="Output"/>). Recursive so it applies to every subcommand.</summary>
    public static readonly Option<bool> JsonOption = new("--json")
    {
        Description = "Emit a machine-readable JSON envelope ({schema_version, ok, data, error}) instead of human text.",
        Recursive = true,
    };

    /// <summary>Global <c>--api-url</c>: override the loopback target (default
    /// <c>http://127.0.0.1:7842</c>). Recursive so it applies to every subcommand.</summary>
    public static readonly Option<string> ApiUrlOption = new("--api-url")
    {
        Description = "Override the local API base URL (default http://127.0.0.1:7842).",
        Recursive = true,
        DefaultValueFactory = _ => ApiClient.DefaultBaseUrl.ToString(),
    };

    /// <summary>Build the root command with its global options. Subcommands are registered by
    /// later tasks (read commands, write commands, status/config/export, installers).</summary>
    public static RootCommand Build()
    {
        var root = new RootCommand(
            "lore — your personal memory layer, from the terminal. A thin client of the local Lore "
            + "API; read commands support --json with a stable, versioned schema and meaningful exit codes.");
        root.Options.Add(JsonOption);
        root.Options.Add(ApiUrlOption);

        // Bare `lore` (no subcommand) prints a short banner. `lore --help` still shows full help.
        // This also exercises the whole pipeline — global options → ApiClient → Output, both modes.
        root.SetAction(parseResult =>
        {
            Output output = CreateOutput(parseResult);
            if (!TryResolveApiUrl(parseResult, output, out Uri apiUrl))
            {
                return ExitCodes.BadUsage;
            }

            using var client = new ApiClient(apiUrl);
            if (output.IsJson)
            {
                output.WriteData(new BannerData("lore", client.BaseUrl.ToString(), "Run 'lore --help' for commands."));
            }
            else
            {
                output.WriteLine("lore — your personal memory layer, from the terminal.");
                output.WriteLine($"Talking to the local Lore API at {client.BaseUrl}.");
                output.WriteLine("Run 'lore --help' to see the available commands.");
            }

            return ExitCodes.Success;
        });

        return root;
    }

    /// <summary>Build the <see cref="Output"/> for a command invocation: human-vs-JSON comes from
    /// the global <c>--json</c> flag; output goes to the console. Shared by every command's action.
    /// Command <em>logic</em> is tested by constructing an <see cref="Output"/> over captured
    /// writers directly, so this console binding stays a thin, untested adapter.</summary>
    public static Output CreateOutput(ParseResult parseResult)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        return new Output(Console.Out, Console.Error, parseResult.GetValue(JsonOption));
    }

    /// <summary>Resolve the effective API URL from <c>--api-url</c>, reporting a bad-usage error
    /// through <paramref name="output"/> if it isn't a valid absolute URL. Shared by every command
    /// so the override is parsed and validated in exactly one place.</summary>
    public static bool TryResolveApiUrl(ParseResult parseResult, Output output, out Uri apiUrl)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(output);

        string raw = parseResult.GetValue(ApiUrlOption) ?? ApiClient.DefaultBaseUrl.ToString();
        if (Uri.TryCreate(raw, UriKind.Absolute, out Uri? parsed)
            && parsed.Scheme is "http" or "https")
        {
            apiUrl = parsed;
            return true;
        }

        output.WriteError("bad_usage", $"--api-url must be an absolute http(s) URL, but got '{raw}'.");
        apiUrl = ApiClient.DefaultBaseUrl;
        return false;
    }

    /// <summary>The bare-<c>lore</c> banner payload, emitted as the <c>--json</c> envelope's data.</summary>
    private sealed record BannerData(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("api_url")] string ApiUrl,
        [property: System.Text.Json.Serialization.JsonPropertyName("hint")] string Hint);
}
