using System.IO.Pipelines;
using Lore.Agent.Inference;
using Lore.Agent.Mcp;
using Lore.Agent.Memory;
using Lore.Agent.Tests.Api;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lore.Agent.Tests.Mcp;

/// <summary>The MCP round-trip integration test (spec 006 T005, acceptance criterion 6): an MCP
/// client drives search -> add -> forget against a seeded store, and the <b>same round-trip runs
/// over both transports</b> — stdio (a stream-pair transport, the framing stdio uses) and
/// Streamable HTTP. One tool implementation behind two transports, proven by one shared
/// assertion body, so neither transport can silently diverge.</summary>
public sealed class McpRoundTripTests
{
    [Fact]
    public async Task Round_trip_over_streamable_http()
    {
        var store = new FakeMemoryService();
        await using LoreApiHarness harness = await LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton<IMemoryService>(store);
                services.AddSingleton<IInferenceBackend>(new StubInferenceBackend("a summary"));
                services.AddLoreMcpHttp();
            },
            app => app.MapLoreMcp());

#pragma warning disable CA2000 // ownership passes to the McpClient, which disposes it.
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(harness.Client.BaseAddress!, McpHttpTransport.Path),
            TransportMode = HttpTransportMode.StreamableHttp,
        });
#pragma warning restore CA2000
        await using McpClient client = await McpClient.CreateAsync(transport);

        await AssertRoundTripAsync(client, store);
    }

    [Fact]
    public async Task Round_trip_over_stdio_stream()
    {
        var store = new FakeMemoryService();

        // Two unidirectional pipes form the duplex channel stdio would otherwise provide via the
        // process's stdin/stdout: one client->server, one server->client.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IMemoryService>(store);
        builder.Services.AddSingleton<IInferenceBackend>(new StubInferenceBackend("a summary"));
        builder.Services
            .AddMcpServer()
            .WithStreamServerTransport(clientToServer.Reader.AsStream(), serverToClient.Writer.AsStream())
            .WithToolsFromAssembly(typeof(LoreTools).Assembly);

        using IHost host = builder.Build();
        await host.StartAsync();

        try
        {
#pragma warning disable CA2000 // ownership passes to the McpClient, which disposes it.
            var transport = new StreamClientTransport(
                serverInput: clientToServer.Writer.AsStream(),
                serverOutput: serverToClient.Reader.AsStream(),
                NullLoggerFactory.Instance);
#pragma warning restore CA2000
            await using McpClient client = await McpClient.CreateAsync(transport);

            await AssertRoundTripAsync(client, store);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>The transport-agnostic round-trip: search finds a seeded memory, add makes a new
    /// one searchable, and forget removes it — all driven through the MCP client.</summary>
    private static async Task AssertRoundTripAsync(McpClient client, FakeMemoryService store)
    {
        string seedId = store.Seed("The user is learning Rust");

        // search: the seeded memory is found.
        CallToolResult search = await client.CallToolAsync(
            "get_context", new Dictionary<string, object?> { ["query"] = "Rust" });
        Assert.NotEqual(true, search.IsError);
        Assert.Contains("Rust", TextOf(search), StringComparison.Ordinal);

        // add: a new memory, written through the client.
        CallToolResult add = await client.CallToolAsync(
            "add_context",
            new Dictionary<string, object?> { ["text"] = "The user prefers dark mode", ["category"] = "preferences" });
        Assert.NotEqual(true, add.IsError);

        // ...and it is now searchable.
        CallToolResult searchAdded = await client.CallToolAsync(
            "get_context", new Dictionary<string, object?> { ["query"] = "dark mode" });
        Assert.Contains("dark mode", TextOf(searchAdded), StringComparison.Ordinal);

        // forget: the new memory is deleted, and the result echoes what was forgotten.
        CallToolResult forget = await client.CallToolAsync(
            "forget", new Dictionary<string, object?> { ["topic"] = "dark mode" });
        Assert.NotEqual(true, forget.IsError);
        Assert.Contains("dark mode", TextOf(forget), StringComparison.Ordinal);
        Assert.Contains("\"forgotten\":true", TextOf(forget).Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        // ...and it no longer turns up in search.
        CallToolResult searchGone = await client.CallToolAsync(
            "get_context", new Dictionary<string, object?> { ["query"] = "dark mode" });
        Assert.DoesNotContain("dark mode", TextOf(searchGone), StringComparison.Ordinal);

        // the seeded memory was never touched.
        Assert.NotNull(await store.GetAsync(seedId));
    }

    private static string TextOf(CallToolResult result) =>
        string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
