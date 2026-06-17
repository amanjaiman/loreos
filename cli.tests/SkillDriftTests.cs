using System.Text.RegularExpressions;

namespace Lore.Cli.Tests;

/// <summary>Drift control for the Lore Agent Skill (spec 008 T004): the skill is prose that names
/// <c>lore</c> commands, and it must stay honest against the actual CLI (spec 007). This test parses
/// every <c>lore &lt;command&gt;</c> the skill invokes (inside code spans) and asserts each is a real
/// top-level command in <see cref="CliRoot"/> — so a renamed or removed command fails CI loudly
/// instead of leaving the skill silently wrong.</summary>
public sealed class SkillDriftTests
{
    // `lore` (lowercase, word boundary) followed by its first token; flags (leading '-') don't match.
    private static readonly Regex LoreInvocation = new(@"\blore\s+([a-z][a-z-]*)", RegexOptions.Compiled);

    [Fact]
    public void Every_command_the_skill_names_exists_in_the_cli()
    {
        string skill = File.ReadAllText(LocateSkill());
        HashSet<string> referenced = ReferencedCommands(skill);

        // Guard against a parsing bug silently passing the check.
        Assert.NotEmpty(referenced);

        var known = KnownCommands();
        foreach (string command in referenced)
        {
            Assert.True(
                known.Contains(command),
                $"SKILL.md references `lore {command}`, which is not a CLI command. " +
                $"Known commands: {string.Join(", ", known.OrderBy(c => c, StringComparer.Ordinal))}.");
        }
    }

    [Fact]
    public void Skill_actually_references_the_core_commands()
    {
        // The skill is useless if it forgot to teach search/add/forget.
        HashSet<string> referenced = ReferencedCommands(File.ReadAllText(LocateSkill()));

        Assert.Contains("search", referenced);
        Assert.Contains("add", referenced);
        Assert.Contains("forget", referenced);
    }

    private static HashSet<string> ReferencedCommands(string markdown)
    {
        // Only look inside code (fenced blocks and inline spans) so prose like "the lore skill"
        // can't masquerade as a command. Join with a non-letter separator so matches don't bleed
        // across spans.
        var code = new List<string>();
        code.AddRange(Matches(markdown, "```.*?```", RegexOptions.Singleline));
        code.AddRange(Matches(markdown, "`[^`\n]+`", RegexOptions.None));
        string joined = string.Join("\n;\n", code);

        return LoreInvocation.Matches(joined)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Matches(string input, string pattern, RegexOptions options) =>
        Regex.Matches(input, pattern, options).Select(match => match.Value);

    private static HashSet<string> KnownCommands() =>
        CliRoot.Build().Subcommands.Select(command => command.Name).ToHashSet(StringComparer.Ordinal);

    // Walk up from the test assembly to the repo root (marked by Lore.sln), then resolve the skill.
    private static string LocateSkill()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lore.sln")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "could not locate the repo root (Lore.sln) from the test directory.");
        string path = Path.Combine(dir!.FullName, "skills", "lore", "SKILL.md");
        Assert.True(File.Exists(path), $"expected the skill at {path}.");
        return path;
    }
}
