using System.Net.Http;
using Lore.Agent.Api;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Import;
using Lore.Agent.Mcp;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lore.Agent;

/// <summary>Entry point and composition root for the Lore agent. Stands up the local
/// API host (constitution §3.1) and the memoryd supervisor (§3.3). Later specs add
/// capture (003) and the full REST API (005) to this same host; <c>--mcp</c> (006)
/// reuses this DI graph without the HTTP listener.</summary>
internal static class Program
{
    private static async Task Main(string[] args)
    {
        // --mcp (spec 006): serve the MCP tools over stdio, reusing the memory DI graph but
        // opening no HTTP listener (constitution §3.3). This is a distinct host, so branch before
        // the WebApplication/Kestrel host is ever built.
        if (args.Contains(McpStdioServer.ModeFlag, StringComparer.Ordinal))
        {
            await McpStdioServer.RunAsync(args).ConfigureAwait(false);
            return;
        }

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // The local API binds loopback only, with a fail-fast assertion (constitution §3.4).
        ApiHost.ConfigureLoopbackBinding(builder);

        // config.json is the real source the /config endpoints (005 T004) read and write, so
        // edits through the API drive the same blocks the rest of the app binds. Optional: a
        // fresh install has none yet.
        builder.Configuration.AddJsonFile(LoreConfig.DefaultPath, optional: true, reloadOnChange: false);

        builder.Services.Configure<MemorydOptions>(builder.Configuration.GetSection("memory"));
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();

        // The memory seam and the supervisor's health probe both target the
        // configured memoryd (embedded sidecar or remote server).
        builder.Services.AddHttpClient<MemorydClient>(ConfigureMemorydHttpClient);
        builder.Services.AddTransient<IMemoryService>(sp => sp.GetRequiredService<MemorydClient>());
        builder.Services.AddHttpClient<IMemorydHealthProbe, HttpMemorydHealthProbe>(ConfigureMemorydHttpClient);

        builder.Services.AddSingleton<MemorydSupervisor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MemorydSupervisor>());

        // A non-blocking readiness snapshot and the log tail, both surfaced by /system (005 T005).
        builder.Services.AddSingleton<IMemorydReadiness, SupervisorReadiness>();
        builder.Services.AddSingleton(new LogTail(LogTail.DefaultPath));

        // The capture pipeline (spec 003): monitor → filter → gate → analyze → Remember().
        // It awaits the memoryd readiness gate before storing and survives memoryd restarts.
        builder.Services.AddCapturePipeline(builder.Configuration);

        // The provider layer (spec 004): the user's one model choice drives both capture
        // analysis (the real IInferenceBackend, replacing the pipeline's placeholder) and
        // memory (memoryd is reconfigured once healthy). Registered after the pipeline so
        // the real backend wins.
        builder.Services.AddProviderLayer(builder.Configuration);

        // Document import (spec 009): the /import pipeline reuses the capture filter and the
        // memory seam, so it is registered after both are in the graph.
        builder.Services.AddDocumentImport(builder.Configuration);

        // The config seam for the /config endpoints (005 T004): reads/writes config.json and
        // relocates any inline key to the credential store registered by the provider layer.
        builder.Services.AddSingleton(sp => new LoreConfig(
            LoreConfig.DefaultPath, sp.GetRequiredService<ICredentialStore>()));

        // The redacting file sink (005 T005 follow-up): write the agent log to the same lore.log
        // LogTail serves at /system/log. Registered as an ILoggerProvider so the logging factory
        // injects the very SecretRegistry the credential store registers keys into — that shared
        // instance is what makes redaction effective (constitution §4.2). Registered after the
        // provider layer so that singleton exists; wrapping FileLoggerProvider means no log line
        // can reach disk with a key in the clear.
        builder.Services.AddSingleton<ILoggerProvider>(sp =>
            new RedactingLoggerProvider(
                new FileLoggerProvider(LogTail.DefaultPath),
                sp.GetRequiredService<SecretRegistry>()));

        // Recall (v2-001): the every-turn hot path behind POST /recall and the MCP tool.
        builder.Services.AddSingleton(
            builder.Configuration.GetSection("recall").Get<Lore.Agent.Recall.RecallOptions>()
                ?? new Lore.Agent.Recall.RecallOptions());
        builder.Services.AddTransient<Lore.Agent.Recall.RecallService>();

        // The generated OpenAPI contract surfaces pin to, served at /openapi.json (005 T007).
        ApiHost.AddOpenApi(builder.Services);

        // The Streamable HTTP MCP transport (006 T003): the same eight tools the stdio server
        // exposes, mounted on this loopback host for URL-based clients.
        builder.Services.AddLoreMcpHttp();

        WebApplication app = builder.Build();

        // Spec 005: every endpoint group is assembled onto this one host (constitution §3.1).
        ApiHost.UseOpenApi(app);
        app.MapLoreApi();
        app.MapLoreMcp(); // 006 T003: Streamable HTTP MCP endpoint at /mcp

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void ConfigureMemorydHttpClient(IServiceProvider services, HttpClient client)
    {
        MemorydOptions options = services.GetRequiredService<IOptions<MemorydOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    }
}
