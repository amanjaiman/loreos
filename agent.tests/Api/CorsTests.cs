using System.Net.Http;
using Lore.Agent.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Lore.Agent.Tests.Api;

/// <summary>Coverage for the CORS policy: without it, the Electron renderer's <c>fetch</c>
/// calls are blocked client-side even though the HTTP response itself succeeds — this is
/// exactly the bug where the agent logs look healthy but the app reports "Lore isn't
/// running". The policy is deliberately origin-limited (never <c>AllowAnyOrigin</c>): this
/// API can read and reset the whole memory store, so an arbitrary web page must not be able
/// to read its responses.</summary>
public sealed class CorsTests
{
    private static Task<LoreApiHarness> StartAsync() => LoreApiHarness.StartAsync(
        services => ApiHost.AddCors(services),
        app =>
        {
            ApiHost.UseCors(app);
            app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        });

    private static HttpRequestMessage HealthRequest(string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    [Fact]
    public async Task Dev_renderer_origin_gets_the_allow_header()
    {
        await using LoreApiHarness harness = await StartAsync();
        using HttpRequestMessage request = HealthRequest(ApiHost.DevRendererOrigin);

        using HttpResponseMessage response = await harness.Client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Origin", out IEnumerable<string>? values));
        Assert.Equal(ApiHost.DevRendererOrigin, Assert.Single(values!));
    }

    [Fact]
    public async Task Packaged_file_origin_gets_the_allow_header()
    {
        // Chromium sends the literal string "null" as Origin for a file:// page — the
        // packaged app's renderer, not the absence of a header.
        await using LoreApiHarness harness = await StartAsync();
        using HttpRequestMessage request = HealthRequest("null");

        using HttpResponseMessage response = await harness.Client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues(
            "Access-Control-Allow-Origin", out IEnumerable<string>? values));
        Assert.Equal("null", Assert.Single(values!));
    }

    [Fact]
    public async Task An_arbitrary_website_origin_gets_no_allow_header()
    {
        // The actual HTTP response still succeeds (the server doesn't refuse the request) —
        // it's the missing header that makes the browser discard the response client-side.
        await using LoreApiHarness harness = await StartAsync();
        using HttpRequestMessage request = HealthRequest("https://evil.example");

        using HttpResponseMessage response = await harness.Client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Non_browser_clients_with_no_origin_header_are_unaffected()
    {
        // The CLI, MCP clients, and curl never send Origin — CORS middleware is a pure
        // no-op for them regardless of the policy.
        await using LoreApiHarness harness = await StartAsync();
        using HttpRequestMessage request = HealthRequest(null);

        using HttpResponseMessage response = await harness.Client.SendAsync(request);

        Assert.True(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
