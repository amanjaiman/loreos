using System.CommandLine;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands.Install;

/// <summary><c>lore mcp install &lt;client&gt;</c> — wire Lore into an MCP client's config by merging a
/// <c>lore</c> server entry (spec 007 T006). The CLI owns the installers (plan: wiring external tools
/// is an admin/UX concern kept in one place). This command resolves the client's config path and the
/// sibling <c>LoreAgent.exe</c>, then hands the merge to <see cref="McpConfigInstaller"/>. It touches
/// the filesystem, not the API, so it doesn't go through the API scaffold.</summary>
internal static class McpInstallCommand
{
    /// <summary>The MCP clients this installer knows how to configure.</summary>
    public static readonly string[] Clients = { "claude-desktop", "claude-code", "cursor" };

    private static readonly Argument<string> ClientArgument = new("client")
    {
        Description = "Which MCP client to configure: claude-desktop, claude-code, or cursor.",
    };

    /// <summary>Build the <c>mcp</c> command group (currently just <c>install</c>).</summary>
    public static Command Build()
    {
        var install = new Command("install", "Add Lore to an MCP client's configuration.")
        {
            ClientArgument,
        };
        install.SetAction(parseResult => Run(
            CliRoot.CreateOutput(parseResult), parseResult.GetValue(ClientArgument)!, ResolveAgentPath()));

        var mcp = new Command("mcp", "Manage how Lore is wired into MCP clients.")
        {
            install,
        };
        return mcp;
    }

    internal static int Run(Output output, string client, string agentExePath)
    {
        if (!TryResolveConfigPath(client, out string configPath))
        {
            output.WriteError(
                "bad_usage", $"unknown client '{client}'. Supported clients: {string.Join(", ", Clients)}.");
            return ExitCodes.BadUsage;
        }

        try
        {
            InstallResult result = McpConfigInstaller.Install(configPath, agentExePath);
            Report(output, client, result);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is McpInstallException or IOException or UnauthorizedAccessException)
        {
            output.WriteError("runtime_error", ex.Message);
            return ExitCodes.RuntimeFailure;
        }
    }

    private static void Report(Output output, string client, InstallResult result)
    {
        if (output.IsJson)
        {
            var data = new JsonObject
            {
                ["client"] = client,
                ["config_path"] = result.ConfigPath,
                ["action"] = ActionLabel(result.Action),
            };
            if (result.BackupPath is not null)
            {
                data["backup_path"] = result.BackupPath;
            }

            output.WriteData(data);
            return;
        }

        switch (result.Action)
        {
            case InstallAction.Unchanged:
                output.WriteLine($"Lore is already configured in {client} ({result.ConfigPath}); nothing to do.");
                break;
            case InstallAction.Created:
                output.WriteLine($"Installed Lore into {client} → {result.ConfigPath}.");
                output.WriteLine("Restart the client to pick up the new server.");
                break;
            default:
                output.WriteLine($"Updated Lore in {client} → {result.ConfigPath}.");
                if (result.BackupPath is not null)
                {
                    output.WriteLine($"Backed up the previous config to {result.BackupPath}.");
                }

                output.WriteLine("Restart the client to pick up the change.");
                break;
        }
    }

    // The stable lower-case action label for the --json envelope.
    private static string ActionLabel(InstallAction action) => action switch
    {
        InstallAction.Created => "created",
        InstallAction.Updated => "updated",
        _ => "unchanged",
    };

    /// <summary>Locate the on-disk config for <paramref name="client"/>. Claude Desktop and Cursor
    /// have a single global config; Claude Code is configured per-project via <c>.mcp.json</c> in the
    /// current directory.</summary>
    internal static bool TryResolveConfigPath(string client, out string configPath)
    {
        switch (client)
        {
            case "claude-desktop":
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Claude", "claude_desktop_config.json");
                return true;
            case "cursor":
                configPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cursor", "mcp.json");
                return true;
            case "claude-code":
                configPath = Path.Combine(Directory.GetCurrentDirectory(), ".mcp.json");
                return true;
            default:
                configPath = string.Empty;
                return false;
        }
    }

    // The CLI (lore.exe) and the agent (LoreAgent.exe) ship side by side, so the agent is the sibling
    // of the running executable — that's the path the MCP client should launch.
    private static string ResolveAgentPath() =>
        Path.Combine(AppContext.BaseDirectory, "LoreAgent.exe");
}
