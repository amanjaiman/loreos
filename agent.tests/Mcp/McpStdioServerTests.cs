using Lore.Agent.Mcp;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace Lore.Agent.Tests.Mcp;

/// <summary>Coverage for the <c>--mcp</c> stdio host (spec 006 T002; v2-001 T008 adds
/// <c>recall</c>). Asserts what <see cref="McpStdioServer.BuildHost"/> wires without
/// starting it (so no memoryd is spawned): the eight tools are present, and — the
/// load-bearing guarantee — the host opens <b>no HTTP listener</b> (criterion 3).</summary>
public sealed class McpStdioServerTests
{
    private static readonly string[] ExpectedTools =
    [
        "recall",
        "get_context",
        "get_recent",
        "get_profile",
        "summarize_profile",
        "add_context",
        "update_context",
        "forget",
    ];

    [Fact]
    public void BuildHost_registers_all_eight_tools()
    {
        using IHost host = McpStdioServer.BuildHost([McpStdioServer.ModeFlag]);

        string[] toolNames = host.Services
            .GetServices<McpServerTool>()
            .Select(tool => tool.ProtocolTool.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedTools.OrderBy(name => name, StringComparer.Ordinal), toolNames);
    }

    [Fact]
    public void BuildHost_opens_no_http_listener()
    {
        using IHost host = McpStdioServer.BuildHost([McpStdioServer.ModeFlag]);

        // A generic host has no Kestrel/IServer; the agent's WebApplication mode is the only one
        // that binds the loopback API (constitution §3.3). If a future edit reintroduced a web
        // host here, IServer would resolve and this would fail.
        Assert.Null(host.Services.GetService<IServer>());
    }
}
