using System.Net.Http;
using Lore.Agent.Capture;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    // The agent's local API binds loopback only (constitution §1.1 / §3.4).
    private const string LocalApiUrl = "http://127.0.0.1:7842";

    private static async Task Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(LocalApiUrl);

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

        WebApplication app = builder.Build();

        // Only /health here; spec 005 maps the full memory API onto this host.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        await app.RunAsync().ConfigureAwait(false);
    }

    private static void ConfigureMemorydHttpClient(IServiceProvider services, HttpClient client)
    {
        MemorydOptions options = services.GetRequiredService<IOptions<MemorydOptions>>().Value;
        client.BaseAddress = options.BaseAddress;
    }
}
