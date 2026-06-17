using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class OpenAiCompatibleBackendTests : IDisposable
{
    private const string SuccessBody =
        """{"choices":[{"message":{"role":"assistant","content":"local note"}}]}""";

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

    // Acceptance criterion 2: openai_compatible reaches a local Ollama endpoint by base
    // URL with no cloud calls. The mocked request proves the routing + payload; the live
    // run against a real Ollama is the documented manual check (docs/providers.md).
    [Fact]
    public async Task Routes_to_base_url_chat_completions_without_a_key()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new OpenAiCompatibleBackend(
            http, new Uri("http://localhost:11434/v1"), apiKey: null, "qwen3:8b", 256);

        string? result = await backend.CompleteAsync(new InferenceRequest("be terse", "what am I doing"));

        Assert.Equal("local note", result);
        Assert.Equal(
            new Uri("http://localhost:11434/v1/chat/completions"), handler.Request!.RequestUri);
        Assert.Null(handler.Request.Headers.Authorization); // keyless local endpoint

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("qwen3:8b", body.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Sends_bearer_auth_when_a_key_is_supplied()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new OpenAiCompatibleBackend(
            http, new Uri("https://openrouter.ai/api/v1/"), "or-key", "some/model", 256);

        await backend.CompleteAsync(new InferenceRequest("s", "u"));

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("or-key", handler.Request.Headers.Authorization.Parameter);
        Assert.Equal(
            new Uri("https://openrouter.ai/api/v1/chat/completions"), handler.Request.RequestUri);
    }

    [Fact]
    public async Task Unreachable_endpoint_throws_unreachable()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException("connection refused"));
        var backend = new OpenAiCompatibleBackend(
            http, new Uri("http://localhost:11434/v1"), null, "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unreachable, ex.Kind);
    }

    [Fact]
    public void Constructor_rejects_a_relative_base_url()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException());
        Assert.Throws<ArgumentException>(
            () => new OpenAiCompatibleBackend(http, new Uri("/v1", UriKind.Relative), null, "m", 64));
    }
}
