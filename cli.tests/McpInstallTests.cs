using System.Text.Json.Nodes;
using Lore.Cli.Commands.Install;

namespace Lore.Cli.Tests;

/// <summary>The MCP installer (spec 007 T006, acceptance criterion 4): merging a <c>lore</c> server
/// entry into a client config is correct, idempotent, non-destructive (other servers and keys
/// survive), and backed up before any change. A malformed config is refused, not clobbered.</summary>
public sealed class McpInstallTests : IDisposable
{
    private const string AgentPath = @"C:\Program Files\Lore\LoreAgent.exe";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lore-mcp-" + Guid.NewGuid().ToString("N"));

    public McpInstallTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private static JsonObject Read(string path) => (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

    [Fact]
    public void Creates_a_new_config_with_the_lore_entry()
    {
        // A nested path also exercises directory creation.
        string path = Path.Combine(_dir, "nested", "claude_desktop_config.json");

        InstallResult result = McpConfigInstaller.Install(path, AgentPath);

        Assert.Equal(InstallAction.Created, result.Action);
        Assert.Null(result.BackupPath);
        JsonObject lore = (JsonObject)Read(path)["mcpServers"]!["lore"]!;
        Assert.Equal(AgentPath, lore["command"]!.GetValue<string>());
        Assert.Equal("--mcp", lore["args"]!.AsArray()[0]!.GetValue<string>());
    }

    [Fact]
    public void Re_running_with_the_same_entry_is_a_no_op()
    {
        string path = Path_("mcp.json");
        McpConfigInstaller.Install(path, AgentPath);
        string afterFirst = File.ReadAllText(path);

        InstallResult second = McpConfigInstaller.Install(path, AgentPath);

        Assert.Equal(InstallAction.Unchanged, second.Action);
        Assert.Null(second.BackupPath);
        Assert.False(File.Exists(path + ".bak")); // nothing changed, so nothing backed up
        Assert.Equal(afterFirst, File.ReadAllText(path));
    }

    [Fact]
    public void Merges_without_clobbering_existing_servers_or_keys()
    {
        string path = Path_("claude_desktop_config.json");
        File.WriteAllText(path,
            "{\"globalShortcut\":\"Ctrl+L\",\"mcpServers\":{\"other\":{\"command\":\"other.exe\"}}}");

        McpConfigInstaller.Install(path, AgentPath);

        JsonObject root = Read(path);
        Assert.Equal("Ctrl+L", root["globalShortcut"]!.GetValue<string>());            // unrelated key kept
        Assert.Equal("other.exe", root["mcpServers"]!["other"]!["command"]!.GetValue<string>()); // other server kept
        Assert.Equal(AgentPath, root["mcpServers"]!["lore"]!["command"]!.GetValue<string>());     // lore added
    }

    [Fact]
    public void Updates_a_stale_entry_and_backs_up_first()
    {
        string path = Path_("mcp.json");
        File.WriteAllText(path,
            "{\"mcpServers\":{\"lore\":{\"command\":\"C:\\\\old\\\\LoreAgent.exe\",\"args\":[\"--mcp\"]}}}");

        InstallResult result = McpConfigInstaller.Install(path, AgentPath);

        Assert.Equal(InstallAction.Updated, result.Action);
        Assert.Equal(path + ".bak", result.BackupPath);
        Assert.True(File.Exists(path + ".bak"));
        Assert.Contains("old", File.ReadAllText(path + ".bak"), StringComparison.Ordinal);       // backup has the old value
        Assert.Equal(AgentPath, Read(path)["mcpServers"]!["lore"]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void Refuses_a_malformed_config_without_touching_it()
    {
        string path = Path_("broken.json");
        File.WriteAllText(path, "{ not valid json");

        Assert.Throws<McpInstallException>(() => McpConfigInstaller.Install(path, AgentPath));
        Assert.Equal("{ not valid json", File.ReadAllText(path)); // untouched
    }

    [Fact]
    public void Refuses_a_config_whose_mcpServers_is_the_wrong_type()
    {
        string path = Path_("weird.json");
        File.WriteAllText(path, "{\"mcpServers\":\"oops\"}");

        Assert.Throws<McpInstallException>(() => McpConfigInstaller.Install(path, AgentPath));
    }

    [Theory]
    [InlineData("claude-desktop", "claude_desktop_config.json")]
    [InlineData("cursor", "mcp.json")]
    [InlineData("claude-code", ".mcp.json")]
    public void Resolves_a_config_path_for_each_known_client(string client, string expectedFileName)
    {
        bool ok = McpInstallCommand.TryResolveConfigPath(client, out string path);

        Assert.True(ok);
        Assert.Equal(expectedFileName, Path.GetFileName(path));
    }

    [Fact]
    public void Rejects_an_unknown_client()
    {
        Assert.False(McpInstallCommand.TryResolveConfigPath("emacs", out _));
    }

    [Fact]
    public void Run_reports_bad_usage_for_an_unknown_client()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        int code = McpInstallCommand.Run(output, "emacs", AgentPath);

        Assert.Equal(ExitCodes.BadUsage, code);
        Assert.Equal("bad_usage", JsonNode.Parse(stdout.ToString())!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Run_installs_claude_code_into_the_current_directory()
    {
        string previous = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_dir);
        try
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: true);

            int code = McpInstallCommand.Run(output, "claude-code", AgentPath);

            Assert.Equal(ExitCodes.Success, code);
            JsonNode data = JsonNode.Parse(stdout.ToString())!["data"]!;
            Assert.Equal("created", data["action"]!.GetValue<string>());
            Assert.True(File.Exists(Path_(".mcp.json")));
        }
        finally
        {
            Directory.SetCurrentDirectory(previous);
        }
    }
}
