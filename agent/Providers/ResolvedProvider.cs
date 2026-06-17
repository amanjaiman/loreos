namespace Lore.Agent.Providers;

/// <summary>A validated, normalized provider configuration — the input the backends
/// (T002/T003) and the test endpoint (T005) consume. Produced by
/// <see cref="ProviderSelector.Resolve"/>; if you hold one, the config was well-formed.
/// <see cref="ApiKeyRef"/> is a credential-store handle, never key material.</summary>
public sealed record ResolvedProvider(
    ProviderKind Kind,
    string Model,
    Uri? BaseUrl,
    string? ApiKeyRef,
    int MaxTokens);
