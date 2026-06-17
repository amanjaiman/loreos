using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>Any OpenAI-compatible chat-completions endpoint reached by base URL (spec 004):
/// Ollama, LM Studio, llama.cpp server, vLLM, OpenRouter, Together, Groq, … — Lore's "direct
/// URL to a hosted model" path, and the way local-first users run with no cloud at all. The
/// key is optional so keyless local servers work; the request/response shape is the same
/// <see cref="OpenAiChatProtocol"/> the first-party OpenAI backend uses.</summary>
public sealed class OpenAiCompatibleBackend : HttpInferenceBackend
{
    private readonly Uri _endpoint;
    private readonly string? _apiKey;

    /// <param name="baseUrl">The endpoint base, e.g. <c>http://localhost:11434/v1</c>.
    /// <c>/chat/completions</c> is appended.</param>
    /// <param name="apiKey">Bearer token, or <c>null</c>/empty for a keyless endpoint.</param>
    public OpenAiCompatibleBackend(
        HttpClient httpClient, Uri baseUrl, string? apiKey, string model, int maxTokens)
        : base(httpClient, model, maxTokens)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (!baseUrl.IsAbsoluteUri)
        {
            throw new ArgumentException("Base URL must be absolute.", nameof(baseUrl));
        }

        // Resolve relative to the base; ensuring a trailing slash keeps the last path
        // segment (e.g. "/v1") instead of replacing it.
        string withSlash = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl.AbsoluteUri : baseUrl.AbsoluteUri + "/";
        _endpoint = new Uri(new Uri(withSlash), "chat/completions");
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
    }

    protected override Uri Endpoint => _endpoint;

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }
    }

    protected override object BuildRequestBody(InferenceRequest request) =>
        OpenAiChatProtocol.BuildBody(Model, MaxTokens, request);

    protected override string? ExtractCompletion(JsonElement root) =>
        OpenAiChatProtocol.ExtractCompletion(root);
}
