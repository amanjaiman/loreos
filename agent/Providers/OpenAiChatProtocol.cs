using System.Text.Json;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>The OpenAI <c>/chat/completions</c> request/response shape, shared by the
/// first-party <see cref="OpenAiBackend"/> and every OpenAI-compatible endpoint
/// (<see cref="OpenAiCompatibleBackend"/>, spec 004 T003) — Ollama, LM Studio, vLLM, and
/// the rest speak this same contract.</summary>
internal static class OpenAiChatProtocol
{
    public static object BuildBody(string model, int maxTokens, InferenceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var messages = new List<object>(2);
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        messages.Add(new { role = "user", content = request.UserPrompt });

        return new
        {
            model,
            max_tokens = maxTokens,
            temperature = request.Temperature,
            messages,
        };
    }

    public static string? ExtractCompletion(JsonElement root)
    {
        // Response shape: { "choices": [ { "message": { "content": "..." } } ] }.
        if (!root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (JsonElement choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out JsonElement message)
                && message.TryGetProperty("content", out JsonElement content)
                && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        return null;
    }
}
