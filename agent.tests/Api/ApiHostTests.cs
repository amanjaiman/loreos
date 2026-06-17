using System.Net;
using System.Net.Http;
using Lore.Agent.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Lore.Agent.Tests.Api;

/// <summary>Guards spec 005 acceptance criterion 1: the local API is reachable on loopback
/// and binding to an external interface is impossible by default. The loopback guard is the
/// real enforcement (it refuses a non-loopback bind at startup); the live-host test proves the
/// running server's only listen address is loopback and that the API is routable there.</summary>
public sealed class ApiHostTests
{
    [Theory]
    [InlineData("http://0.0.0.0:7842")]
    [InlineData("http://+:7842")]
    [InlineData("http://*:7842")]
    [InlineData("http://192.168.1.50:7842")]
    [InlineData("http://10.0.0.5:7842")]
    [InlineData("http://203.0.113.7:7842")]
    [InlineData("http://[::]:7842")]
    [InlineData("http://127.0.0.1:7842;http://0.0.0.0:7842")] // any one external bind is rejected
    public void EnsureLoopbackOnly_rejects_non_loopback_binds(string serverUrls)
    {
        Assert.Throws<InvalidOperationException>(() => ApiHost.EnsureLoopbackOnly(serverUrls));
    }

    [Theory]
    [InlineData("http://127.0.0.1:7842")]
    [InlineData("http://localhost:7842")]
    [InlineData("http://[::1]:7842")]
    [InlineData("http://127.0.0.5:7842")]
    [InlineData("http://127.0.0.1:7842;http://[::1]:7842")]
    public void EnsureLoopbackOnly_accepts_loopback_binds(string serverUrls)
    {
        ApiHost.EnsureLoopbackOnly(serverUrls); // does not throw
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EnsureLoopbackOnly_ignores_empty(string? serverUrls)
    {
        ApiHost.EnsureLoopbackOnly(serverUrls); // nothing configured, nothing to reject
    }

    [Fact]
    public async Task Health_is_routable_and_the_only_bind_is_loopback()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        // Ephemeral loopback port so the test never collides with a running agent on 7842.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication app = builder.Build();
        app.MapLoreApi();
        await app.StartAsync();

        // The running server listens on exactly one address, and it is loopback — so it is
        // not reachable on any external interface.
        string address = Assert.Single(app.Urls);
        var bound = new Uri(address);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(bound.Host)));

        using var client = new HttpClient();
        HttpResponseMessage response = await client.GetAsync(new Uri(bound, "/health"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await app.StopAsync();
    }
}
