using System.Text.Json.Nodes;
using Lore.Cli.Commands.Install;

namespace Lore.Cli.Tests;

/// <summary>The skills installer (spec 007 T007, acceptance criterion 5): copy the bundled skill
/// package (nested tree and all) into the client's skills dir, idempotently. The package content is
/// spec 008's; these tests use a synthetic package to prove the placement mechanism.</summary>
public sealed class SkillsInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lore-skills-" + Guid.NewGuid().ToString("N"));
    private readonly string _source;
    private readonly string _dest;

    public SkillsInstallTests()
    {
        _source = Path.Combine(_root, "src", "lore");
        _dest = Path.Combine(_root, "dest", "lore");

        // A small synthetic skill package: a top-level SKILL.md and a nested reference file.
        Directory.CreateDirectory(Path.Combine(_source, "reference"));
        File.WriteAllText(Path.Combine(_source, "SKILL.md"), "# Lore skill\n");
        File.WriteAllText(Path.Combine(_source, "reference", "commands.md"), "lore search ...\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Copies_the_whole_package_tree()
    {
        SkillsInstallResult result = SkillsInstaller.Install(_source, _dest);

        Assert.False(result.AlreadyPresent);
        Assert.Equal(2, result.FilesCopied);
        Assert.True(File.Exists(Path.Combine(_dest, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(_dest, "reference", "commands.md")));
    }

    [Fact]
    public void Re_running_is_idempotent()
    {
        SkillsInstaller.Install(_source, _dest);
        SkillsInstallResult second = SkillsInstaller.Install(_source, _dest);

        Assert.True(second.AlreadyPresent);
        Assert.Equal("# Lore skill\n", File.ReadAllText(Path.Combine(_dest, "SKILL.md")));
    }

    [Fact]
    public void Refreshes_stale_content_on_reinstall()
    {
        SkillsInstaller.Install(_source, _dest);
        File.WriteAllText(Path.Combine(_dest, "SKILL.md"), "stale, edited by hand");

        SkillsInstaller.Install(_source, _dest);

        Assert.Equal("# Lore skill\n", File.ReadAllText(Path.Combine(_dest, "SKILL.md")));
    }

    [Fact]
    public void Reports_a_missing_package_clearly()
    {
        string missing = Path.Combine(_root, "does-not-exist");

        SkillsInstallException ex = Assert.Throws<SkillsInstallException>(
            () => SkillsInstaller.Install(missing, _dest));
        Assert.Contains("not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolves_the_claude_code_skills_dir()
    {
        bool ok = SkillsInstallCommand.TryResolveSkillsDir("claude-code", out string dir);

        Assert.True(ok);
        Assert.EndsWith(Path.Combine(".claude", "skills", "lore"), dir, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_rejects_an_unknown_client()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        int code = SkillsInstallCommand.Run(output, "vim", _source);

        Assert.Equal(ExitCodes.BadUsage, code);
        Assert.Equal("bad_usage", JsonNode.Parse(stdout.ToString())!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Run_reports_a_missing_package_as_runtime_failure()
    {
        var stderr = new StringWriter();
        var output = new Output(new StringWriter(), stderr, json: false);

        int code = SkillsInstallCommand.Run(output, "claude-code", Path.Combine(_root, "nope"));

        Assert.Equal(ExitCodes.RuntimeFailure, code);
    }
}
