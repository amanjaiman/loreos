using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;

namespace Lore.Cli.Tests;

/// <summary>End-to-end exit-code contract (spec 007 T005, acceptance criteria 3 &amp; 6): driving the
/// CLI exactly as a shell would — through <see cref="CliRoot.InvokeAsync"/> — every failure class
/// returns its documented code, and an unreachable agent is friendly rather than a stack trace. The
/// HTTP-status mappings (404 → 4, 400 → 2) are covered against a stubbed API in
/// <see cref="CommandSupportTests"/> and the per-command tests.</summary>
public sealed class ExitCodeTests
{
    private static async Task<(int Code, string Out, string Err)> RunCli(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int code = await CliRoot.InvokeAsync(args);
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    // A loopback port that nothing is listening on — bind to :0 to claim a free one, then release
    // it. Connecting there yields a connection refusal, i.e. "Lore isn't running".
    private static int ClosedLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public async Task Help_exits_zero()
    {
        (int code, _, _) = await RunCli("--help");
        Assert.Equal(ExitCodes.Success, code);
    }

    [Fact]
    public async Task Bare_invocation_exits_zero()
    {
        (int code, string outText, _) = await RunCli();
        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("lore", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_command_is_bad_usage()
    {
        (int code, _, string err) = await RunCli("definitely-not-a-command");
        Assert.Equal(ExitCodes.BadUsage, code);
        Assert.NotEqual(string.Empty, err);
    }

    [Fact]
    public async Task Missing_required_argument_is_bad_usage()
    {
        // `search` requires a <query>.
        (int code, _, _) = await RunCli("search");
        Assert.Equal(ExitCodes.BadUsage, code);
    }

    [Fact]
    public async Task Bad_api_url_is_bad_usage()
    {
        (int code, _, _) = await RunCli("status", "--api-url", "not-a-url");
        Assert.Equal(ExitCodes.BadUsage, code);
    }

    [Fact]
    public async Task Agent_down_is_friendly_and_exits_three()
    {
        int port = ClosedLoopbackPort();

        (int code, _, string err) = await RunCli("status", "--api-url", $"http://127.0.0.1:{port}");

        Assert.Equal(ExitCodes.AgentUnreachable, code);
        Assert.Contains("isn't running", err, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", err, StringComparison.Ordinal); // no stack trace
    }

    [Fact]
    public async Task Agent_down_in_json_mode_emits_an_error_envelope()
    {
        int port = ClosedLoopbackPort();

        (int code, string outText, _) = await RunCli("status", "--api-url", $"http://127.0.0.1:{port}", "--json");

        Assert.Equal(ExitCodes.AgentUnreachable, code);
        JsonNode envelope = JsonNode.Parse(outText)!;
        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Equal("agent_unreachable", envelope["error"]!["code"]!.GetValue<string>());
    }
}
