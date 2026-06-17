using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Tests.Api;

/// <summary>A live, in-process local-API host for endpoint tests (spec 005). It boots a real
/// Kestrel on an ephemeral loopback port, lets a test register the fakes its group needs and
/// map that group, and hands back an <see cref="HttpClient"/> pointed at it — so tests exercise
/// real routing, model binding, serialization, and status codes, not handler internals.</summary>
internal sealed class LoreApiHarness : IAsyncDisposable
{
    private readonly WebApplication _app;

    private LoreApiHarness(WebApplication app, HttpClient client)
    {
        _app = app;
        Client = client;
    }

    /// <summary>An HTTP client whose base address is the running host.</summary>
    public HttpClient Client { get; }

    public static async Task<LoreApiHarness> StartAsync(
        Action<IServiceCollection> configureServices,
        Action<WebApplication> map)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders(); // keep test output to the assertions
        configureServices(builder.Services);

        WebApplication app = builder.Build();
        map(app);
        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        return new LoreApiHarness(app, client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.DisposeAsync();
    }
}
