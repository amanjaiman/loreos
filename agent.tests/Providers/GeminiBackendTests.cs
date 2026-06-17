using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class GeminiBackendTests : IDisposable
{
    private const string SuccessBody =
        """{"candidates":[{"content":{"role":"model","parts":[{"text":"a "},{"text":"note"}]}}]}""";

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
    public async Task Posts_generate_content_with_the_key_in_a_header_and_returns_text()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new GeminiBackend(http, "g-key", "gemini-2.5-flash", 200);

        string? result = await backend.CompleteAsync(new InferenceRequest("be terse", "what am I doing"));

        Assert.Equal("a note", result);
        Assert.Equal(
            new Uri("https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent"),
            handler.Request!.RequestUri);
        Assert.Equal("g-key", handler.Request.Headers.GetValues("x-goog-api-key").Single());

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = body.RootElement;
        Assert.Equal(
            "be terse",
            root.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(
            "what am I doing",
            root.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(200, root.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32());
    }

    [Fact]
    public async Task Omits_system_instruction_when_there_is_no_system_prompt()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new GeminiBackend(http, "k", "gemini-2.5-flash", 64);

        await backend.CompleteAsync(new InferenceRequest(string.Empty, "hi"));

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        Assert.False(body.RootElement.TryGetProperty("systemInstruction", out _));
    }

    [Fact]
    public async Task Bad_key_reported_as_http_400_throws_unauthorized()
    {
        (HttpClient http, _) = Capture(
            HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}""");
        var backend = new GeminiBackend(http, "bad", "gemini-2.5-flash", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task Unknown_model_404_throws_model_not_found()
    {
        (HttpClient http, _) = Capture(
            HttpStatusCode.NotFound, """{"error":{"message":"models/nope is not found"}}""");
        var backend = new GeminiBackend(http, "k", "nope", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.ModelNotFound, ex.Kind);
    }

    [Fact]
    public async Task Transport_failure_throws_unreachable()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException("no dns"));
        var backend = new GeminiBackend(http, "k", "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unreachable, ex.Kind);
    }

    [Fact]
    public void Constructor_rejects_a_blank_key()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException());
        Assert.Throws<ArgumentException>(() => new GeminiBackend(http, "  ", "m", 64));
    }
}
