using System.CommandLine;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands.Install;

/// <summary>
/// Provider-neutral setup for agent tools. A portable user skill is the default surface;
/// Claude Desktop receives MCP only when it is installed because it cannot run the CLI skill.
/// The older protocol-specific commands remain available for compatibility.
/// </summary>
internal static class ConnectCommand
{
    private static readonly Option<bool> AllOption = new("--all")
    {
        Description = "Also configure supported clients that were not detected.",
    };

    public static Command Build()
    {
        var command = new Command(
            "connect",
            "Connect Lore to your agent tools with the portable skill and any required MCP shim.")
        {
            AllOption,
        };
        command.SetAction(parseResult => Run(
            CliRoot.CreateOutput(parseResult),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ResolveSkillSource(),
            ResolveAgentPath(),
            parseResult.GetValue(AllOption)));
        return command;
    }

    internal static int Run(
        Output output,
        string userProfile,
        string appData,
        string skillSource,
        string agentExePath,
        bool connectAll)
    {
        ArgumentNullException.ThrowIfNull(output);
        var connected = new JsonArray();

        try
        {
            InstallSkill("Agent Skills", Path.Combine(userProfile, ".agents", "skills", "lore"));
            // Claude Code's native user root remains necessary on versions that do not scan the
            // shared convention. A copy is deliberate on Windows: it works without symlink rights.
            InstallSkill("Claude Code", Path.Combine(userProfile, ".claude", "skills", "lore"));

            foreach ((string client, string home) in NativeSkillTargets(userProfile))
            {
                if (connectAll || Directory.Exists(home))
                {
                    InstallSkill(client, Path.Combine(home, "skills", "lore"));
                }
            }

            string claudeDir = Path.Combine(appData, "Claude");
            if (connectAll || Directory.Exists(claudeDir))
            {
                string configPath = Path.Combine(claudeDir, "claude_desktop_config.json");
                InstallResult result = McpConfigInstaller.Install(configPath, agentExePath);
                connected.Add(Item("Claude Desktop", "MCP", result.ConfigPath, ActionLabel(result.Action)));
            }

            if (output.IsJson)
            {
                output.WriteData(new JsonObject { ["connected"] = connected });
            }
            else
            {
                output.WriteLine("Lore is connected to your agent tools:");
                foreach (JsonNode? item in connected)
                {
                    output.WriteLine($"  {item!["client"]} — {item["surface"]} ({item["action"]})");
                }
                output.WriteLine("Restart open agent tools so they discover the Lore skill.");
            }

            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is SkillsInstallException or McpInstallException
            or IOException or UnauthorizedAccessException)
        {
            output.WriteError("runtime_error", ex.Message);
            return ExitCodes.RuntimeFailure;
        }

        void InstallSkill(string client, string destination)
        {
            SkillsInstallResult result = SkillsInstaller.Install(skillSource, destination);
            connected.Add(Item(
                client,
                "Agent Skill",
                result.DestinationDir,
                result.AlreadyPresent ? "updated" : "created"));
        }
    }

    private static JsonObject Item(string client, string surface, string path, string action) => new()
    {
        ["client"] = client,
        ["surface"] = surface,
        ["path"] = path,
        ["action"] = action,
    };

    private static string ActionLabel(InstallAction action) => action switch
    {
        InstallAction.Created => "created",
        InstallAction.Updated => "updated",
        _ => "unchanged",
    };

    private static string ResolveAgentPath() => Path.Combine(AppContext.BaseDirectory, "LoreAgent.exe");

    private static string ResolveSkillSource() => Path.Combine(AppContext.BaseDirectory, "skills", "lore");

    private static IEnumerable<(string Client, string Home)> NativeSkillTargets(string userProfile)
    {
        yield return ("Cursor", Path.Combine(userProfile, ".cursor"));
        yield return ("Gemini CLI", Path.Combine(userProfile, ".gemini"));
        yield return ("Hermes", Path.Combine(userProfile, ".hermes"));
        yield return ("Pi", Path.Combine(userProfile, ".pi", "agent"));
    }
}
