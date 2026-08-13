using System.Text.Json.Nodes;
using Lore.Cli.Commands.Install;

namespace Lore.Cli.Tests;

public sealed class ConnectCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lore-connect-" + Guid.NewGuid().ToString("N"));
    private readonly string _skill;

    public ConnectCommandTests()
    {
        _skill = Path.Combine(_root, "source", "lore");
        Directory.CreateDirectory(_skill);
        File.WriteAllText(Path.Combine(_skill, "SKILL.md"), "---\nname: lore\n---\n");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Connect_installs_shared_and_claude_skill_without_creating_undetected_mcp_config()
    {
        string home = Path.Combine(_root, "home");
        string appData = Path.Combine(_root, "appdata");
        var stdout = new StringWriter();

        int code = ConnectCommand.Run(
            new Output(stdout, new StringWriter(), json: true),
            home, appData, _skill, Path.Combine(_root, "LoreAgent.exe"), connectAll: false);

        Assert.Equal(ExitCodes.Success, code);
        Assert.True(File.Exists(Path.Combine(home, ".agents", "skills", "lore", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home, ".claude", "skills", "lore", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(appData, "Claude", "claude_desktop_config.json")));
        Assert.Equal(2, JsonNode.Parse(stdout.ToString())!["data"]!["connected"]!.AsArray().Count);
    }

    [Fact]
    public void Connect_all_adds_claude_desktop_and_is_idempotent()
    {
        string home = Path.Combine(_root, "home");
        string appData = Path.Combine(_root, "appdata");
        string agent = Path.Combine(_root, "LoreAgent.exe");

        Assert.Equal(ExitCodes.Success, Run(home, appData, agent));
        Assert.Equal(ExitCodes.Success, Run(home, appData, agent));

        Assert.True(File.Exists(Path.Combine(home, ".cursor", "skills", "lore", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home, ".gemini", "skills", "lore", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home, ".hermes", "skills", "lore", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(home, ".pi", "agent", "skills", "lore", "SKILL.md")));

        string config = Path.Combine(appData, "Claude", "claude_desktop_config.json");
        Assert.Equal(agent, JsonNode.Parse(File.ReadAllText(config))!["mcpServers"]!["lore"]!["command"]!.GetValue<string>());
    }

    private int Run(string home, string appData, string agent) => ConnectCommand.Run(
        new Output(new StringWriter(), new StringWriter(), json: false),
        home, appData, _skill, agent, connectAll: true);
}
