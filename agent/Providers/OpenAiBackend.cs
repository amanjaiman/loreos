using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>The user's OpenAI model, via the chat-completions API (spec 004). Auth is a
/// bearer token. The request/response shape is shared with the OpenAI-compatible backend
/// through <see cref="OpenAiChatProtocol"/>.</summary>
public sealed class OpenAiBackend : HttpInferenceBackend
{
    private static readonly Uri ChatEndpoint = new("https://api.openai.com/v1/chat/completions");

    private readonly string _apiKey;

    public OpenAiBackend(HttpClient httpClient, string apiKey, string model, int maxTokens)
        : base(httpClient, model, maxTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
    }

    protected override Uri Endpoint => ChatEndpoint;

    protected override string EndpointLabel => "OpenAI";

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    protected override object BuildRequestBody(InferenceRequest request) =>
        OpenAiChatProtocol.BuildBody(Model, MaxTokens, request);

    protected override string? ExtractCompletion(JsonElement root) =>
        OpenAiChatProtocol.ExtractCompletion(root);
}
