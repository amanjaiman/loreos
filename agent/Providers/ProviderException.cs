namespace Lore.Agent.Providers;

/// <summary>A categorized model-call failure (spec 004). Backends throw this so the
/// capture loop sees a normal exception (it owns resilience, spec 003 T008) and the
/// test-connection endpoint (T005) can turn <see cref="Kind"/> into a specific,
/// actionable message. The message itself never contains key material (constitution §4).</summary>
public sealed class ProviderException : Exception
{
    public ProviderException()
    {
    }

    public ProviderException(string message)
        : base(message)
    {
    }

    public ProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ProviderException(ProviderErrorKind kind, string message)
        : base(message) => Kind = kind;

    public ProviderException(ProviderErrorKind kind, string message, Exception innerException)
        : base(message, innerException) => Kind = kind;

    /// <summary>The failure category. Defaults to <see cref="ProviderErrorKind.BadResponse"/>.</summary>
    public ProviderErrorKind Kind { get; } = ProviderErrorKind.BadResponse;
}
