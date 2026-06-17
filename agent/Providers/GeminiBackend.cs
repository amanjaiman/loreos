using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>The user's Google Gemini model, via the Generative Language
/// <c>generateContent</c> API (spec 004). The key travels in the <c>x-goog-api-key</c>
/// header (not the URL) so it never lands in a request log; the model id is part of the
/// path.</summary>
public sealed class GeminiBackend : HttpInferenceBackend
{
    private const string ApiBase = "https://generativelanguage.googleapis.com/v1beta/models/";

    private readonly string _apiKey;
    private readonly Uri _endpoint;

    public GeminiBackend(HttpClient httpClient, string apiKey, string model, int maxTokens)
        : base(httpClient, model, maxTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
        _endpoint = new Uri($"{ApiBase}{Uri.EscapeDataString(model)}:generateContent");
    }

    protected override Uri Endpoint => _endpoint;

    protected override string EndpointLabel => "Gemini";

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add("x-goog-api-key", _apiKey);
    }

    protected override object BuildRequestBody(InferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new
        {
            systemInstruction = string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? null
                : new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = request.UserPrompt } } },
            },
            generationConfig = new { temperature = request.Temperature, maxOutputTokens = MaxTokens },
        };
    }

    protected override string? ExtractCompletion(JsonElement root)
    {
        // Response shape: { "candidates": [ { "content": { "parts": [ { "text": "..." } ] } } ] }.
        if (!root.TryGetProperty("candidates", out JsonElement candidates)
            || candidates.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        foreach (JsonElement candidate in candidates.EnumerateArray())
        {
            if (!candidate.TryGetProperty("content", out JsonElement content)
                || !content.TryGetProperty("parts", out JsonElement parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }

            // The first candidate carries the completion; later ones are alternatives.
            if (builder.Length > 0)
            {
                break;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
