using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;

namespace Lore.Cli.Tests;

/// <summary>Transport behavior of the CLI's one seam to the local API (spec 007 T001): it returns
/// the status and body verbatim for commands to map, and translates a connection failure into the
/// "Lore isn't running" signal (<see cref="AgentUnreachableException"/>, acceptance criterion 6).</summary>
public sealed class ApiClientTests
{
    [Fact]
    public async Task SendAsync_returns_status_and_body_for_a_success_response()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"ok\"}", Encoding.UTF8, "application/json"),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);

        ApiResponse response = await client.SendAsync(HttpMethod.Get, "/health");

        Assert.True(response.IsSuccess);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", response.ReadJson()!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task SendAsync_targets_the_base_url_and_path()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(new Uri("http://127.0.0.1:7842"), http);

        await client.SendAsync(HttpMethod.Post, "/memories/search", new { query = "x" });

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://127.0.0.1:7842/memories/search", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task SendAsync_preserves_a_non_success_status()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"nope\"}"),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);

        ApiResponse response = await client.SendAsync(HttpMethod.Get, "/memories/missing");

        Assert.False(response.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SendAsync_translates_a_refused_connection_into_AgentUnreachable()
    {
        // "Lore isn't running" presents as a socket-level connection refusal.
        using var handler = new StubHttpMessageHandler(_ =>
            throw new HttpRequestException("refused", new SocketException((int)SocketError.ConnectionRefused)));
        using var http = new HttpClient(handler);
        var target = new Uri("http://127.0.0.1:7842");
        using var client = new ApiClient(target, http);

        AgentUnreachableException ex = await Assert.ThrowsAsync<AgentUnreachableException>(
            () => client.SendAsync(HttpMethod.Get, "/health"));

        Assert.Equal(target, ex.BaseUrl);
    }

    [Fact]
    public void ReadJson_returns_null_for_a_non_json_body()
    {
        var response = new ApiResponse(HttpStatusCode.OK, "# a markdown export, not json");
        Assert.Null(response.ReadJson());
    }
}
