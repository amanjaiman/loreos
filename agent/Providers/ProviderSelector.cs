using System.Globalization;

namespace Lore.Agent.Providers;

/// <summary>Turns the raw <c>provider</c> config into exactly one validated
/// <see cref="ResolvedProvider"/>. This is the whole of provider selection: a pure
/// function of config with <b>no priority chain, no fallback, no implicit default</b>
/// (plan "config is the whole truth"; constitution "deterministic selection"). Missing
/// or malformed config throws <see cref="ProviderConfigurationException"/> with an
/// actionable message instead of silently picking something.</summary>
public static class ProviderSelector
{
    private const string ValidTypes = "anthropic, openai, gemini, openai_compatible";

    /// <summary>Validate and normalize <paramref name="options"/>.</summary>
    /// <exception cref="ProviderConfigurationException">The config is missing or
    /// malformed; the message says exactly what to fix.</exception>
    public static ResolvedProvider Resolve(ProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ProviderKind kind = ParseKind(options.Type);

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ProviderConfigurationException(
                $"Provider '{options.Type}' has no model. Set provider.model to the model id to call.");
        }

        if (options.MaxTokens <= 0)
        {
            throw new ProviderConfigurationException(
                $"provider.max_tokens must be a positive number; got {options.MaxTokens.ToString(CultureInfo.InvariantCulture)}.");
        }

        Uri? baseUrl = ResolveBaseUrl(kind, options.BaseUrl);
        string? apiKeyRef = RequireApiKeyRefWhenNeeded(kind, options.ApiKeyRef);

        return new ResolvedProvider(kind, options.Model.Trim(), baseUrl, apiKeyRef, options.MaxTokens);
    }

    private static ProviderKind ParseKind(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ProviderConfigurationException(
                $"No provider is configured. Set provider.type to one of: {ValidTypes}.");
        }

        return type.Trim().ToUpperInvariant() switch
        {
            "ANTHROPIC" => ProviderKind.Anthropic,
            "OPENAI" => ProviderKind.OpenAi,
            "GEMINI" => ProviderKind.Gemini,
            "OPENAI_COMPATIBLE" => ProviderKind.OpenAiCompatible,
            _ => throw new ProviderConfigurationException(
                $"Unknown provider type '{type}'. Valid types: {ValidTypes}."),
        };
    }

    // openai_compatible is reached by a user-supplied base URL; the first-party
    // providers use their own well-known endpoints, so any base_url is ignored.
    private static Uri? ResolveBaseUrl(ProviderKind kind, string? rawBaseUrl)
    {
        if (kind != ProviderKind.OpenAiCompatible)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(rawBaseUrl))
        {
            throw new ProviderConfigurationException(
                "Provider 'openai_compatible' requires a base URL. Set provider.base_url "
                + "(e.g. http://localhost:11434/v1 for Ollama).");
        }

        if (!Uri.TryCreate(rawBaseUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ProviderConfigurationException(
                $"provider.base_url is not a valid http(s) URL: '{rawBaseUrl}'.");
        }

        return uri;
    }

    // Keyless local endpoints (openai_compatible against Ollama/LM Studio) need no key;
    // every cloud provider does, and the key is stored by handle, never inline.
    private static string? RequireApiKeyRefWhenNeeded(ProviderKind kind, string? apiKeyRef)
    {
        if (kind == ProviderKind.OpenAiCompatible)
        {
            return string.IsNullOrWhiteSpace(apiKeyRef) ? null : apiKeyRef.Trim();
        }

        if (string.IsNullOrWhiteSpace(apiKeyRef))
        {
            throw new ProviderConfigurationException(
                $"Provider '{kind}' requires an API key. Set "
                + "provider.api_key_ref to the credential-store handle that holds it.");
        }

        return apiKeyRef.Trim();
    }
}
