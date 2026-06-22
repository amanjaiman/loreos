using Lore.Agent.Memory;
using Microsoft.Extensions.Configuration;

namespace Lore.Agent.Providers;

/// <summary>A typed view over the optional <c>embedder</c> config block (spec 013) — the
/// escape hatch for providers without first-party embeddings (Anthropic; chat-only
/// OpenAI-compatible endpoints like OpenRouter/Groq; a self-host with no embed model).
/// When a <see cref="Type"/> is set it overrides the provider-derived default and is
/// forwarded to memoryd; when empty the bridge falls back to the known-provider default
/// or memoryd's <c>follow_provider</c>.
///
/// <para>Shape (config.json, top level alongside <c>provider</c>):</para>
/// <code>
/// "embedder": {
///   "type": "openai",                        // openai | ollama
///   "model": "text-embedding-3-small",
///   "base_url": "https://api.openai.com/v1", // optional; the embeddings endpoint
///   "api_key_ref": "lore/embedder",          // optional; reuses the provider key when omitted
///   "dims": 1536                             // vector dimension; sizes the Qdrant collection
/// }
/// </code></summary>
public sealed class EmbedderOptions
{
    /// <summary>Embedder kind memoryd understands: <c>openai</c> (any OpenAI-compatible
    /// embeddings endpoint) or <c>ollama</c>. Empty means "no explicit embedder."</summary>
    public string? Type { get; set; }

    /// <summary>The embedding model id (e.g. <c>text-embedding-3-small</c>,
    /// <c>nomic-embed-text</c>).</summary>
    public string? Model { get; set; }

    /// <summary>Base URL of the embeddings endpoint. Optional for first-party OpenAI;
    /// required to point at a self-hosted or third-party endpoint.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1056:URI-like properties should not be strings",
        Justification = "Raw config value; normalized to a Uri when bridged to memoryd.")]
    [ConfigurationKeyName("base_url")]
    public string? BaseUrl { get; set; }

    /// <summary>Credential-store handle for the embedder's key, when it targets a
    /// different keyed host than the provider. Omitted ⇒ reuse the provider key. Never
    /// the key itself.</summary>
    [ConfigurationKeyName("api_key_ref")]
    public string? ApiKeyRef { get; set; }

    /// <summary>Vector dimension; must match the model so Qdrant's collection is sized
    /// correctly. When omitted memoryd resolves it from its known-dimensions map.</summary>
    public int? Dims { get; set; }

    /// <summary>True when an explicit embedder is configured (a non-empty type).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Type);

    /// <summary>Translate into the memoryd wire model, attaching the already-resolved
    /// <paramref name="apiKey"/> (or null to reuse the provider key). Returns null when no
    /// explicit embedder is configured, so the bridge can fall back to its default.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization", "CA1308:Normalize strings to uppercase",
        Justification = "memoryd's wire protocol matches the lowercase literals 'openai'/'ollama'.")]
    public MemoryEmbedderConfig? ToMemoryEmbedder(string? apiKey)
    {
        if (!IsConfigured)
        {
            return null;
        }

        Uri? baseUrl = null;
        if (!string.IsNullOrWhiteSpace(BaseUrl) && Uri.TryCreate(BaseUrl.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            baseUrl = parsed;
        }

        return new MemoryEmbedderConfig(
            Type!.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(Model) ? null : Model.Trim(),
            baseUrl,
            Dims,
            string.IsNullOrEmpty(apiKey) ? null : apiKey);
    }
}
