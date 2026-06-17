using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class AnthropicBackendTests : IDisposable
{
    private const string SuccessBody =
        """{"content":[{"type":"text","text":"a first-person observation"}]}""";

    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }
    }

    private (HttpClient Http, CapturingHttpHandler Handler) Capture(HttpStatusCode status, string body)
    {
        var handler = new CapturingHttpHandler(status, body);
        var http = new HttpClient(handler);
        _clients.Add(http);
        return (http, handler);
    }

    private (HttpClient Http, ThrowingHttpHandler Handler) Throwing(Exception exception)
    {
        var handler = new ThrowingHttpHandler(exception);
        var http = new HttpClient(handler);
        _clients.Add(http);
        return (http, handler);
    }

    [Fact]
    public async Task Sends_the_messages_request_and_returns_the_text()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new AnthropicBackend(http, "secret-key", "claude-haiku-4-5", 256);

        string? result = await backend.CompleteAsync(new InferenceRequest("be terse", "what am I doing"));

        Assert.Equal("a first-person observation", result);
        Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), handler.Request!.RequestUri);
        Assert.Equal("secret-key", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.Request.Headers.GetValues("anthropic-version").Single());

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = body.RootElement;
        Assert.Equal("claude-haiku-4-5", root.GetProperty("model").GetString());
        Assert.Equal(256, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("be terse", root.GetProperty("system").GetString());
        Assert.Equal("what am I doing", root.GetProperty("messages")[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Omits_the_system_field_when_there_is_no_system_prompt()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new AnthropicBackend(http, "k", "claude", 64);

        await backend.CompleteAsync(new InferenceRequest(string.Empty, "hi"));

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.TryGetProperty("system", out _));
    }

    [Fact]
    public async Task Concatenates_multiple_text_blocks_and_ignores_non_text()
    {
        const string multi =
            """{"content":[{"type":"text","text":"one "},{"type":"tool_use"},{"type":"text","text":"two"}]}""";
        (HttpClient http, _) = Capture(HttpStatusCode.OK, multi);
        var backend = new AnthropicBackend(http, "k", "m", 64);

        string? result = await backend.CompleteAsync(new InferenceRequest("s", "u"));

        Assert.Equal("one two", result);
    }

    [Fact]
    public async Task Empty_content_yields_null()
    {
        (HttpClient http, _) = Capture(HttpStatusCode.OK, """{"content":[]}""");
        var backend = new AnthropicBackend(http, "k", "m", 64);

        Assert.Null(await backend.CompleteAsync(new InferenceRequest("s", "u")));
    }

    [Fact]
    public async Task Unauthorized_status_throws_unauthorized()
    {
        (HttpClient http, _) = Capture(HttpStatusCode.Unauthorized, """{"error":"bad key"}""");
        var backend = new AnthropicBackend(http, "k", "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task Not_found_status_throws_model_not_found()
    {
        (HttpClient http, _) = Capture(HttpStatusCode.NotFound, """{"error":"no model"}""");
        var backend = new AnthropicBackend(http, "k", "nope", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.ModelNotFound, ex.Kind);
    }

    [Fact]
    public async Task Transport_failure_throws_unreachable()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException("no route"));
        var backend = new AnthropicBackend(http, "k", "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unreachable, ex.Kind);
    }

    [Fact]
    public void Constructor_rejects_a_blank_key()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException());
        Assert.Throws<ArgumentException>(() => new AnthropicBackend(http, " ", "m", 64));
    }
}
