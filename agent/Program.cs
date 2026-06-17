using System.Net.Http;
using Lore.Agent.Api;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        // The config seam for the /config endpoints (005 T004): reads/writes config.json and
        // relocates any inline key to the credential store registered by the provider layer.
        builder.Services.AddSingleton(sp => new LoreConfig(
            LoreConfig.DefaultPath, sp.GetRequiredService<ICredentialStore>()));

        // The generated OpenAPI contract surfaces pin to, served at /openapi.json (005 T007).
        ApiHost.AddOpenApi(builder.Services);

        WebApplication app = builder.Build();

        // Spec 005: every endpoint group is assembled onto this one host (constitution §3.1).
        ApiHost.UseOpenApi(app);
        app.MapLoreApi();

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void ConfigureMemorydHttpClient(IServiceProvider services, HttpClient client)
    {
        MemorydOptions options = services.GetRequiredService<IOptions<MemorydOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    }
}
