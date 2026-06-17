namespace Lore.Agent.Providers;

/// <summary>Thrown when the <c>provider</c> config block is missing or malformed. The
/// message is user-actionable ("configure a provider", "set provider.base_url", …) —
/// selection fails closed with guidance rather than silently falling back to a default
/// (spec 004 acceptance criterion; constitution "deterministic selection").</summary>
public sealed class ProviderConfigurationException : Exception
{
    public ProviderConfigurationException()
    {
    }

    public ProviderConfigurationException(string message)
        : base(message)
    {
    }

    public ProviderConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
