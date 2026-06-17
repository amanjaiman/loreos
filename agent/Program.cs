using System.Net.Http;
using Lore.Agent.Api;
using Lore.Agent.Capture;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
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

        builder.Services.Configure<MemorydOptions>(builder.Configuration.GetSection("memory"));
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();

        // The memory seam and the supervisor's health probe both target the
        // configured memoryd (embedded sidecar or remote server).
        builder.Services.AddHttpClient<MemorydClient>(ConfigureMemorydHttpClient);
        builder.Services.AddTransient<IMemoryService>(sp => sp.GetRequiredService<MemorydClient>());
        builder.Services.AddHttpClient<IMemorydHealthProbe, HttpMemorydHealthProbe>(ConfigureMemorydHttpClient);

        builder.Services.AddSingleton<MemorydSupervisor>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MemorydSupervisor>());

        // The capture pipeline (spec 003): monitor → filter → gate → analyze → Remember().
        // It awaits the memoryd readiness gate before storing and survives memoryd restarts.
        builder.Services.AddCapturePipeline(builder.Configuration);

        // The provider layer (spec 004): the user's one model choice drives both capture
        // analysis (the real IInferenceBackend, replacing the pipeline's placeholder) and
        // memory (memoryd is reconfigured once healthy). Registered after the pipeline so
        // the real backend wins.
        builder.Services.AddProviderLayer(builder.Configuration);

        WebApplication app = builder.Build();

        // Spec 005: every endpoint group is assembled onto this one host (constitution §3.1).
        app.MapLoreApi();

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void ConfigureMemorydHttpClient(IServiceProvider services, HttpClient client)
    {
        MemorydOptions options = services.GetRequiredService<IOptions<MemorydOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    }
}
