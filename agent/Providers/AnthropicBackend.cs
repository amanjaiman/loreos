using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>The user's Claude model, via the Anthropic messages API (spec 004). Auth is
/// the <c>x-api-key</c> header; the system prompt is a top-level field and the user
/// content is a single user message.</summary>
public sealed class AnthropicBackend : HttpInferenceBackend
{
    // The endpoint is fixed; only the user's key and model id vary. Pinning the API
    // version keeps the request shape stable regardless of server-side defaults.
    private static readonly Uri MessagesEndpoint = new("https://api.anthropic.com/v1/messages");
    private const string AnthropicVersion = "2023-06-01";

    private readonly string _apiKey;

    public AnthropicBackend(HttpClient httpClient, string apiKey, string model, int maxTokens)
        : base(httpClient, model, maxTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        _apiKey = apiKey;
    }

    protected override Uri Endpoint => MessagesEndpoint;

    protected override string EndpointLabel => "Anthropic";

    protected override void ApplyHeaders(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
    }

    protected override object BuildRequestBody(InferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new
        {
            model = Model,
            max_tokens = MaxTokens,
            temperature = request.Temperature,
            system = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.UserPrompt } },
        };
    }

    protected override string? ExtractCompletion(JsonElement root)
    {
        // Response shape: { "content": [ { "type": "text", "text": "..." }, ... ] }.
        // Concatenate every text block; ignore non-text blocks (e.g. tool use).
        if (!root.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new System.Text.StringBuilder();
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.ValueEquals("text")
                && block.TryGetProperty("text", out JsonElement text)
                && text.ValueKind == JsonValueKind.String)
            {
                builder.Append(text.GetString());
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
