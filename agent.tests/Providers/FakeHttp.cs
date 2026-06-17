using System.Net;
using System.Net.Http;
using System.Text;

namespace Lore.Agent.Tests.Providers;

/// <summary>Records the outgoing request (headers + body) and returns a canned response,
/// so a backend can be tested with no real provider.</summary>
internal sealed class CapturingHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _body;

    public CapturingHttpHandler(HttpStatusCode status, string body)
    {
        _status = status;
        _body = body;
    }

    public HttpRequestMessage? Request { get; private set; }

    public string? RequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        if (request.Content is not null)
        {
            RequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_body, Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>Fails every send with a fixed exception, to exercise transport failures
/// (an unreachable endpoint).</summary>
internal sealed class ThrowingHttpHandler : HttpMessageHandler
{
    private readonly Exception _exception;

    public ThrowingHttpHandler(Exception exception) => _exception = exception;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) => throw _exception;
}
