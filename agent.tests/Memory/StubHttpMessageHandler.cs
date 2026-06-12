using System.Net;
using System.Net.Http;
using System.Text;

namespace Lore.Agent.Tests.Memory;

/// <summary>A test double for <see cref="HttpMessageHandler"/> that records the last
/// request and returns a canned response, so <c>MemorydClient</c> can be tested with
/// no real memoryd.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> responder) =>
        _responder = responder;

    public HttpMethod? LastMethod { get; private set; }

    public Uri? LastUri { get; private set; }

    public string? LastBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastMethod = request.Method;
        LastUri = request.RequestUri;
        if (request.Content is not null)
        {
            LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        (HttpStatusCode status, string body) = _responder(request);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
