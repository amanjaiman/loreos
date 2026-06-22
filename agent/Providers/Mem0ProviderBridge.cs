using Lore.Agent.Memory;

namespace Lore.Agent.Providers;

/// <summary>Translates Lore's one provider choice into the config memoryd needs (spec 004
/// T006), so a user configures a model <b>once</b> and it drives both capture analysis and
/// memory. memoryd's engine speaks <c>openai | anthropic | ollama | openai_compatible</c>;
/// this bridge maps Lore's four kinds onto those, and leaves the embedder as
/// <c>follow_provider</c> so memoryd uses the provider's own embeddings when it has them
/// (OpenAI) and the validated local default otherwise (acceptance criterion 5).</summary>
public static class Mem0ProviderBridge
{
    // Gemini has no native path in memoryd, but it exposes an OpenAI-compatible endpoint;
    // routing it through openai_compatible lets one provider choice still drive memory.
    private static readonly Uri GeminiOpenAiBaseUrl =
        new("https://generativelanguage.googleapis.com/v1beta/openai/");

    // Gemini's embeddings via the same OpenAI-compatible endpoint (spec 013). The model id
    // and its 768-dim vector are pinned so Qdrant is sized correctly; the embedder reuses
    // the provider key (same host), so no second key is needed.
    private const string GeminiEmbedModel = "text-embedding-004";
    private const int GeminiEmbedDims = 768;

    /// <summary>Build the memoryd <c>POST /config</c> payload for <paramref name="provider"/>.
    /// <paramref name="apiKey"/> is the key already resolved from the credential store
    /// (null for a keyless local endpoint); it travels only over loopback to memoryd.
    /// <paramref name="explicitEmbedder"/> is the user-configured embedder (spec 013) when
    /// present; it overrides the provider-derived default.</summary>
    public static MemoryConfig ToMemoryConfig(
        ResolvedProvider provider, string? apiKey, string dataDir,
        MemoryEmbedderConfig? explicitEmbedder = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDir);

        MemoryProviderConfig memProvider = provider.Kind switch
        {
            ProviderKind.Anthropic => new MemoryProviderConfig("anthropic", provider.Model, ApiKey: apiKey),
            ProviderKind.OpenAi => new MemoryProviderConfig("openai", provider.Model, ApiKey: apiKey),
            ProviderKind.Gemini => new MemoryProviderConfig(
                "openai_compatible", provider.Model, GeminiOpenAiBaseUrl, apiKey),
            ProviderKind.OpenAiCompatible => MapOpenAiCompatible(provider, apiKey),
            _ => throw new ProviderConfigurationException($"Unsupported provider kind: {provider.Kind}."),
        };

        // Embedder precedence (spec 013): an explicit user-configured embedder wins; else a
        // known-provider default (Gemini's own embeddings); else null, leaving memoryd's
        // follow_provider to use the provider's first-party embeddings or error actionably.
        MemoryEmbedderConfig? embedder = explicitEmbedder ?? DefaultEmbedder(provider.Kind);
        return new MemoryConfig(memProvider, dataDir, embedder);
    }

    // The provider-derived embedder for providers whose first-party embeddings Lore knows.
    // Gemini's OpenAI-compatible endpoint serves embeddings too, so route them there and let
    // memoryd reuse the provider key (same host). OpenAI is left to memoryd's follow_provider
    // (which already selects text-embedding-3-small). Everything else returns null.
    private static MemoryEmbedderConfig? DefaultEmbedder(ProviderKind kind) => kind switch
    {
        ProviderKind.Gemini => new MemoryEmbedderConfig(
            "openai", GeminiEmbedModel, GeminiOpenAiBaseUrl, GeminiEmbedDims),
        _ => null,
    };

    private static MemoryProviderConfig MapOpenAiCompatible(ResolvedProvider provider, string? apiKey)
    {
        if (!string.IsNullOrEmpty(apiKey))
        {
            // Keyed OpenAI-compatible server (OpenRouter, Together, a secured vLLM, …).
            return new MemoryProviderConfig("openai_compatible", provider.Model, provider.BaseUrl, apiKey);
        }

        // Keyless local: memoryd's openai_compatible path requires a key, so use its native
        // Ollama path. Ollama's native API base is the URL without the OpenAI /v1 suffix.
        return new MemoryProviderConfig("ollama", provider.Model, StripOpenAiSuffix(provider.BaseUrl!));
    }

    private static Uri StripOpenAiSuffix(Uri baseUrl)
    {
        string trimmed = baseUrl.AbsoluteUri.TrimEnd('/');
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^"/v1".Length];
        }

        return trimmed.Length == 0 ? new Uri(baseUrl.GetLeftPart(UriPartial.Authority)) : new Uri(trimmed);
    }
}
