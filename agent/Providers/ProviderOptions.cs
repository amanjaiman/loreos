using Microsoft.Extensions.Configuration;

namespace Lore.Agent.Providers;

/// <summary>A typed view over the <c>provider</c> config block — the single source of
/// truth for which model Lore calls (spec 004). Raw and unvalidated as bound from
/// config; <see cref="ProviderSelector"/> turns it into a validated
/// <see cref="ResolvedProvider"/>.
///
/// <para>Shape (see plan.md):</para>
/// <code>
/// "provider": {
///   "type": "openai_compatible",            // anthropic | openai | gemini | openai_compatible
///   "model": "qwen3:8b",
///   "base_url": "http://localhost:11434/v1", // required for openai_compatible; ignored otherwise
///   "api_key_ref": "lore/provider",          // Credential Manager handle; "" for keyless local
///   "max_tokens": 1024                       // optional; default 1024
/// }
/// </code></summary>
public sealed class ProviderOptions
{
    /// <summary>Provider kind: <c>anthropic</c>, <c>openai</c>, <c>gemini</c>, or
    /// <c>openai_compatible</c>. Case-insensitive. Empty until the user configures one.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The model id to call (e.g. <c>claude-haiku-4-5</c>, <c>qwen3:8b</c>).</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Base URL of the endpoint. Required for <c>openai_compatible</c>; ignored
    /// for the first-party providers, which use their own well-known endpoints. Kept as a
    /// raw string (not <see cref="Uri"/>) so <see cref="ProviderSelector"/> can turn a
    /// malformed value into an actionable message instead of a binder exception.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1056:URI-like properties should not be strings",
        Justification = "Raw config value; validated and normalized to a Uri by ProviderSelector.")]
    [ConfigurationKeyName("base_url")]
    public string? BaseUrl { get; set; }

    /// <summary>Handle into the OS credential store (spec 004 T004) where the API key
    /// lives. Never the key itself. May be empty for a keyless local endpoint.</summary>
    [ConfigurationKeyName("api_key_ref")]
    public string? ApiKeyRef { get; set; }

    /// <summary>Upper bound on tokens generated per call. Capture-analysis completions
    /// are short; the test-connection prompt is tiny. Anthropic's messages API requires
    /// it, so it is carried here for every backend.</summary>
    [ConfigurationKeyName("max_tokens")]
    public int MaxTokens { get; set; } = 1024;
}
