using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Mcp;

/// <summary>The Streamable HTTP MCP transport (spec 006 T003): the same eight
/// <see cref="LoreTools"/> the stdio server exposes, mounted on the local API host (005) for
/// clients that prefer a URL over spawning a process. One tool implementation, two transports —
/// no logic divergence (plan).
///
/// <para>It inherits the host's <b>loopback-only</b> bind (constitution §3.4): the API binds
/// <c>127.0.0.1</c> and nothing here widens it, so the MCP endpoint is reachable only from the
/// machine. The tools resolve <c>IMemoryService</c> / <c>IInferenceBackend</c> from the same DI
/// graph the REST endpoints use.</para></summary>
public static class McpHttpTransport
{
    /// <summary>The loopback path the Streamable HTTP endpoint is mounted at: clients connect to
    /// <c>http://127.0.0.1:7842/mcp</c>.</summary>
    public const string Path = "/mcp";

    /// <summary>Register the MCP server, the Streamable HTTP transport, and the eight tools into
    /// the host's services. Call before <c>Build()</c>.</summary>
    public static IServiceCollection AddLoreMcpHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(typeof(LoreTools).Assembly);
        return services;
    }

    /// <summary>Mount the Streamable HTTP endpoint at <see cref="Path"/>. Call after
    /// <see cref="AddLoreMcpHttp"/> has registered the services.</summary>
    public static IEndpointRouteBuilder MapLoreMcp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapMcp(Path);
        return endpoints;
    }
}
