using System.CommandLine;
using System.Text.Json.Nodes;

namespace Lore.Cli.Commands.Install;

/// <summary><c>lore skills install &lt;client&gt;</c> — install Lore's Agent Skill into a client's
/// skills directory (spec 007 T007). The CLI places the package; spec 008 authors its content. This
/// resolves the client's skills dir and the bundled package, then hands the copy to
/// <see cref="SkillsInstaller"/>. Filesystem work, not the API, so it doesn't use the API scaffold.</summary>
internal static class SkillsInstallCommand
{
    /// <summary>The clients this installer knows how to place the skill for.</summary>
    public static readonly string[] Clients = { "claude-code" };

    private static readonly Argument<string> ClientArgument = new("client")
    {
        Description = "Which client to install the skill for (claude-code).",
    };

    /// <summary>Build the <c>skills</c> command group (currently just <c>install</c>).</summary>
    public static Command Build()
    {
        var install = new Command("install", "Install the Lore Agent Skill into a client.")
        {
            ClientArgument,
        };
        install.SetAction(parseResult => Run(
            CliRoot.CreateOutput(parseResult), parseResult.GetValue(ClientArgument)!, ResolveSkillSource()));

        var skills = new Command("skills", "Manage Lore's Agent Skill in your tools.")
        {
            install,
        };
        return skills;
    }

    internal static int Run(Output output, string client, string sourceDir)
    {
        if (!TryResolveSkillsDir(client, out string destDir))
        {
            output.WriteError(
                "bad_usage", $"unknown client '{client}'. Supported clients: {string.Join(", ", Clients)}.");
            return ExitCodes.BadUsage;
        }

        try
        {
            SkillsInstallResult result = SkillsInstaller.Install(sourceDir, destDir);
            Report(output, client, result);
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is SkillsInstallException or IOException or UnauthorizedAccessException)
        {
            output.WriteError("runtime_error", ex.Message);
            return ExitCodes.RuntimeFailure;
        }
    }

    private static void Report(Output output, string client, SkillsInstallResult result)
    {
        if (output.IsJson)
        {
            output.WriteData(new JsonObject
            {
                ["client"] = client,
                ["skills_dir"] = result.DestinationDir,
                ["files_copied"] = result.FilesCopied,
                ["action"] = result.AlreadyPresent ? "updated" : "created",
            });
            return;
        }

        string verb = result.AlreadyPresent ? "Updated" : "Installed";
        output.WriteLine($"{verb} the Lore skill for {client} → {result.DestinationDir} ({result.FilesCopied} files).");
        output.WriteLine("Restart the client (or reload its skills) to pick it up.");
    }

    /// <summary>Locate the skills directory for <paramref name="client"/>. Claude Code reads user
    /// skills from <c>~/.claude/skills</c>; Lore installs under a <c>lore/</c> subfolder there.</summary>
    internal static bool TryResolveSkillsDir(string client, out string skillsDir)
    {
        switch (client)
        {
            case "claude-code":
                skillsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".claude", "skills", "lore");
                return true;
            default:
                skillsDir = string.Empty;
                return false;
        }
    }

    // The skill package ships alongside the CLI under skills/lore (delivered by spec 008).
    private static string ResolveSkillSource() =>
        Path.Combine(AppContext.BaseDirectory, "skills", "lore");
}
