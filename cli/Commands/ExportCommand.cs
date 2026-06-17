using System.CommandLine;
using System.Net.Http;

namespace Lore.Cli.Commands;

/// <summary><c>lore export [--format json|markdown]</c> — take everything Lore knows about you, a
/// thin shim over <c>GET /export/json</c> / <c>GET /export/markdown</c>. Unlike the read commands,
/// export writes the API's document <b>raw</b> to stdout (it is not wrapped in the <c>--json</c>
/// envelope), because its whole purpose is redirection: <c>lore export &gt; me.json</c>. Failures
/// still go through the shared error path. The export is memory content, not config, so it carries
/// no key material to scrub.</summary>
internal static class ExportCommand
{
    private static readonly Option<string> FormatOption = new("--format")
    {
        Description = "Export format: json or markdown (default json).",
        DefaultValueFactory = _ => "json",
    };

    public static Command Build()
    {
        var command = new Command("export", "Export all your memories as JSON or Markdown.")
        {
            FormatOption,
        };

        command.SetAction((parseResult, ct) => CommandSupport.RunAsync(
            parseResult,
            (client, output, token) => RunAsync(client, output, parseResult.GetValue(FormatOption)!, token),
            ct));

        return command;
    }

    public static async Task<int> RunAsync(
        ApiClient client, Output output, string format, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(output);

        string normalized = format.Trim().ToUpperInvariant();
        string path = normalized switch
        {
            "JSON" => "/export/json",
            "MARKDOWN" or "MD" => "/export/markdown",
            _ => string.Empty,
        };

        if (path.Length == 0)
        {
            output.WriteError("bad_usage", $"--format must be 'json' or 'markdown', but got '{format}'.");
            return ExitCodes.BadUsage;
        }

        ApiResponse response = await client
            .SendAsync(HttpMethod.Get, path, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            return CommandSupport.Fail(output, response);
        }

        // Emit the document exactly as the API produced it, with a single trailing newline.
        output.Write(response.Body);
        if (!response.Body.EndsWith('\n'))
        {
            output.WriteLine();
        }

        return ExitCodes.Success;
    }
}
