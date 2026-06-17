using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class OpenAiBackendTests : IDisposable
{
    private const string SuccessBody =
        """{"choices":[{"message":{"role":"assistant","content":"distilled note"}}]}""";

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
    public async Task Sends_chat_completions_with_bearer_auth_and_returns_content()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new OpenAiBackend(http, "sk-test", "gpt-4o-mini", 128);

        string? result = await backend.CompleteAsync(new InferenceRequest("be terse", "summarize"));

        Assert.Equal("distilled note", result);
        Assert.Equal(new Uri("https://api.openai.com/v1/chat/completions"), handler.Request!.RequestUri);
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.Request.Headers.Authorization.Parameter);

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        JsonElement root = body.RootElement;
        Assert.Equal("gpt-4o-mini", root.GetProperty("model").GetString());
        Assert.Equal(128, root.GetProperty("max_tokens").GetInt32());
        JsonElement messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("be terse", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("summarize", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Omits_the_system_message_when_there_is_no_system_prompt()
    {
        (HttpClient http, CapturingHttpHandler handler) = Capture(HttpStatusCode.OK, SuccessBody);
        var backend = new OpenAiBackend(http, "k", "gpt", 64);

        await backend.CompleteAsync(new InferenceRequest("   ", "hello"));

        using JsonDocument body = JsonDocument.Parse(handler.RequestBody!);
        JsonElement messages = body.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Unauthorized_status_throws_unauthorized()
    {
        (HttpClient http, _) = Capture(HttpStatusCode.Unauthorized, "{}");
        var backend = new OpenAiBackend(http, "k", "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unauthorized, ex.Kind);
    }

    [Fact]
    public async Task Bad_request_about_a_model_throws_model_not_found()
    {
        (HttpClient http, _) = Capture(
            HttpStatusCode.BadRequest, """{"error":{"message":"The model 'x' does not exist"}}""");
        var backend = new OpenAiBackend(http, "k", "x", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.ModelNotFound, ex.Kind);
    }

    [Fact]
    public async Task Transport_failure_throws_unreachable()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException("refused"));
        var backend = new OpenAiBackend(http, "k", "m", 64);

        ProviderException ex = await Assert.ThrowsAsync<ProviderException>(
            () => backend.CompleteAsync(new InferenceRequest("s", "u")));
        Assert.Equal(ProviderErrorKind.Unreachable, ex.Kind);
    }

    [Fact]
    public void Constructor_rejects_a_blank_key()
    {
        (HttpClient http, _) = Throwing(new HttpRequestException());
        Assert.Throws<ArgumentException>(() => new OpenAiBackend(http, "", "m", 64));
    }
}
