using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>Builds the one <see cref="IInferenceBackend"/> a <see cref="ResolvedProvider"/>
/// names, resolving its <c>api_key_ref</c> to the actual key from the credential store
/// (spec 004). Abstracted so the test-connection flow can be exercised with a fake.</summary>
public interface IProviderBackendFactory
{
    /// <summary>Construct the backend for <paramref name="provider"/>.</summary>
    /// <exception cref="ProviderConfigurationException">A required key is missing from the
    /// credential store.</exception>
    IInferenceBackend Create(ResolvedProvider provider);
}
