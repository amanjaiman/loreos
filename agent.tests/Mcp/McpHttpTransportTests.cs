using Lore.Agent.Inference;
using Lore.Agent.Mcp;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Api;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lore.Agent.Tests.Mcp;

/// <summary>Integration coverage for the Streamable HTTP transport (spec 006 T003,
/// acceptance criterion 2). Boots the loopback API host with the MCP transport mounted, then
/// drives a real MCP client over Streamable HTTP: the eight tools are listed, and a
/// representative read and write are invoked end-to-end. The full search -> add -> forget
/// round-trip across both transports lands in T005.</summary>
public sealed class McpHttpTransportTests
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
    public async Task All_eight_tools_are_listed_over_streamable_http()
    {
        var memory = new FakeMemoryService();
        await using LoreApiHarness harness = await StartMcpHostAsync(memory);
        await using McpClient client = await ConnectAsync(harness);

        IList<McpClientTool> tools = await client.ListToolsAsync();

        Assert.Equal(
            ExpectedTools.OrderBy(name => name, StringComparer.Ordinal),
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task Get_context_returns_seeded_memories_over_streamable_http()
    {
        var memory = new FakeMemoryService();
        memory.Seed("The user is using PostgreSQL");
        await using LoreApiHarness harness = await StartMcpHostAsync(memory);
        await using McpClient client = await ConnectAsync(harness);

        CallToolResult result = await client.CallToolAsync(
            "get_context",
            new Dictionary<string, object?> { ["query"] = "PostgreSQL" });

        Assert.NotEqual(true, result.IsError);
        string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.Contains("PostgreSQL", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_context_writes_through_to_the_store_over_streamable_http()
    {
        var memory = new FakeMemoryService();
        await using LoreApiHarness harness = await StartMcpHostAsync(memory);
        await using McpClient client = await ConnectAsync(harness);

        CallToolResult result = await client.CallToolAsync(
            "add_context",
            new Dictionary<string, object?> { ["text"] = "The user prefers TypeScript", ["category"] = "preferences" });

        Assert.NotEqual(true, result.IsError);
        IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync();
        Assert.Equal("The user prefers TypeScript", Assert.Single(all).Memory);
    }

    private static Task<LoreApiHarness> StartMcpHostAsync(IMemoryService memory) =>
        LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton(memory);
                services.AddSingleton<IInferenceBackend>(new StubInferenceBackend("a summary"));
                services.AddLoreMcpHttp();
            },
            app => app.MapLoreMcp());

    private static async Task<McpClient> ConnectAsync(LoreApiHarness harness)
    {
        // The McpClient takes ownership of the transport and disposes it when the test disposes
        // the client (await using). CA2000 can't see that ownership transfer across CreateAsync.
#pragma warning disable CA2000 // Dispose objects before losing scope — ownership passes to the client.
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(harness.Client.BaseAddress!, McpHttpTransport.Path),
            TransportMode = HttpTransportMode.StreamableHttp,
        });
#pragma warning restore CA2000
        return await McpClient.CreateAsync(transport);
    }
}
