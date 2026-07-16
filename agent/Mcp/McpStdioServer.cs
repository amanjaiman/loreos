using System.Net.Http;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Mcp;

/// <summary>The <c>--mcp</c> entrypoint (spec 006 T002): an stdio MCP server that reuses the
/// agent's 002 memory DI graph and runs the eight <see cref="LoreTools"/> over stdio JSON-RPC.
///
/// <para>It deliberately builds a <b>plain generic host, never a <c>WebApplication</c></b>, so
/// <b>no Kestrel/HTTP listener is opened</b> (constitution §3.3; acceptance criterion 3) — in
/// this mode stdout belongs entirely to the MCP transport, and every log is routed to stderr so
/// a stray log line can't corrupt the protocol stream. It registers the memory seam and the
/// provider layer (so <c>summarize_profile</c> has a model and memoryd is pointed at the user's
/// provider) but <b>not</b> the capture pipeline: <c>--mcp</c> is a serving process a client
/// spawns, not a second ambient capturer.</para></summary>
internal static class McpStdioServer
{
    /// <summary>The CLI switch that selects this mode.</summary>
    public const string ModeFlag = "--mcp";

    /// <summary>Build the host and run it until the transport closes (the client disconnects) or
    /// the process is asked to stop.</summary>
    public static async Task RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using IHost host = BuildHost(args);
        await host.RunAsync().ConfigureAwait(false);
    }

    /// <summary>Compose the <c>--mcp</c> host. Separated from <see cref="RunAsync"/> so a test can
    /// assert what it wires — the eight tools present and, crucially, no HTTP server registered
    /// (acceptance criterion 3) — without starting memoryd.</summary>
    public static IHost BuildHost(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // The bare "--mcp" switch is the mode selector, not host configuration; strip it so the
        // command-line config provider doesn't reject it as an unrecognized argument.
        string[] hostArgs = args.Where(arg => !string.Equals(arg, ModeFlag, StringComparison.Ordinal)).ToArray();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(hostArgs);

        // config.json is the same source the agent and the /config endpoints read (optional on a
        // fresh install).
        builder.Configuration.AddJsonFile(LoreConfig.DefaultPath, optional: true, reloadOnChange: false);

        // stdout is the MCP JSON-RPC channel — route all logging to stderr so it can never
        // interleave with protocol frames (constitution §3.3).
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        // --- the 002 memory DI graph (mirrors Program.cs, minus the HTTP host and capture) ---
        builder.Services.Configure<MemorydOptions>(builder.Configuration.GetSection("memory"));
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddHttpClient<MemorydClient>(ConfigureMemorydHttpClient);
        builder.Services.AddTransient<IMemoryService>(sp => sp.GetRequiredService<MemorydClient>());
        builder.Services.AddHttpClient<IMemorydHealthProbe, HttpMemorydHealthProbe>(ConfigureMemorydHttpClient);

        builder.Services.AddSingleton<MemorydSupervisor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MemorydSupervisor>());

        builder.Services.AddSingleton<IMemorydReadiness, SupervisorReadiness>();
        // The readiness bridge the provider layer's Mem0Configurator awaits. Normally registered
        // by the capture pipeline; wired directly here since --mcp does not run capture.
        builder.Services.AddSingleton<IReadinessSignal, MemorydReadinessSignal>();
        builder.Services.AddSingleton(new LogTail(LogTail.DefaultPath));

        // The provider layer: gives summarize_profile its IInferenceBackend and points memoryd at
        // the user's model once healthy (spec 004). No capture pipeline precedes it, so its
        // IInferenceBackend is the only one — exactly what the tool resolves.
        builder.Services.AddProviderLayer(builder.Configuration);

        // Recall (v2-001): the every-turn tool's service. TimeProvider is normally
        // registered by the capture pipeline, which --mcp deliberately does not run.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(
            builder.Configuration.GetSection("recall").Get<Recall.RecallOptions>()
                ?? new Recall.RecallOptions());
        builder.Services.AddTransient<Recall.RecallService>();

        // The MCP server and the eight tools, over stdio.
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly(typeof(LoreTools).Assembly);

        return builder.Build();
    }

    private static void ConfigureMemorydHttpClient(IServiceProvider services, HttpClient client)
    {
        MemorydOptions options = services.GetRequiredService<IOptions<MemorydOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    }
}
