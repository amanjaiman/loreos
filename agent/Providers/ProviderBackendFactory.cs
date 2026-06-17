using System.Net.Http;
using Lore.Agent.Config;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>Maps a validated <see cref="ResolvedProvider"/> to the concrete backend,
/// pulling the API key from the credential store by handle (spec 004). This is the only
/// place the four backend types are constructed; selection upstream already guaranteed the
/// config is well-formed, so any remaining failure is a missing stored key.</summary>
public sealed class ProviderBackendFactory : IProviderBackendFactory
{
    /// <summary>Named <see cref="HttpClient"/> the backends use; the host configures its
    /// timeout so a "test connection" against a dead host fails fast.</summary>
    public const string HttpClientName = "provider";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialStore _credentials;

    public ProviderBackendFactory(IHttpClientFactory httpClientFactory, ICredentialStore credentials)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(credentials);
        _httpClientFactory = httpClientFactory;
        _credentials = credentials;
    }

    public IInferenceBackend Create(ResolvedProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        string? key = ResolveKey(provider);

        return provider.Kind switch
        {
            ProviderKind.Anthropic => new AnthropicBackend(http, key!, provider.Model, provider.MaxTokens),
            ProviderKind.OpenAi => new OpenAiBackend(http, key!, provider.Model, provider.MaxTokens),
            ProviderKind.Gemini => new GeminiBackend(http, key!, provider.Model, provider.MaxTokens),
            ProviderKind.OpenAiCompatible => new OpenAiCompatibleBackend(
                http, provider.BaseUrl!, key, provider.Model, provider.MaxTokens),
            _ => throw new ProviderConfigurationException($"Unsupported provider kind: {provider.Kind}."),
        };
    }

    // Keyless local endpoints (openai_compatible with no handle) resolve to null. Every
    // other provider was required by the selector to carry a handle, so a null/empty value
    // here means the key was never stored.
    private string? ResolveKey(ResolvedProvider provider)
    {
        if (provider.ApiKeyRef is null)
        {
            return null;
        }

        string? key = _credentials.Read(provider.ApiKeyRef);
        if (string.IsNullOrEmpty(key))
        {
            throw new ProviderConfigurationException(
                $"No API key is stored under handle '{provider.ApiKeyRef}'. Save the key in the "
                + "credential store, or correct provider.api_key_ref.");
        }

        return key;
    }
}
