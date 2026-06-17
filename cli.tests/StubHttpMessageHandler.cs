using System.Net.Http;

namespace Lore.Cli.Tests;

/// <summary>A test double for <see cref="HttpClient"/>: every request is handed to a responder
/// the test supplies, which returns a response or throws (e.g. a connection failure). Lets
/// <see cref="ApiClient"/> tests exercise transport behavior without a live server.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    /// <summary>The most recent request the handler saw, for asserting method/URL/body.</summary>
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;

        // A responder that throws (e.g. a simulated connection refusal) surfaces synchronously
        // here; HttpClient observes it just as it would a real transport failure.
        return Task.FromResult(_responder(request));
    }
}
