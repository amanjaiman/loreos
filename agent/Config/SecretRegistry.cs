using System.Collections.Concurrent;

namespace Lore.Agent.Config;

/// <summary>The set of secret values currently in play (API keys resolved from the
/// credential store). It exists so the log pipeline can scrub them: even if a key reaches
/// a log message by accident, <see cref="Redact"/> removes it before it is written
/// (constitution §4.2, "no secrets in logs"). Defense in depth — the code is also written
/// never to log keys in the first place.</summary>
public sealed class SecretRegistry
{
    /// <summary>What a redacted secret is replaced with.</summary>
    public const string Placeholder = "***REDACTED***";

    // A set keyed by the secret value; the value is irrelevant. Concurrent because keys
    // are registered on resolution and read by every log call.
    private readonly ConcurrentDictionary<string, byte> _secrets = new(StringComparer.Ordinal);

    /// <summary>Register a secret so it will be scrubbed from logs. Empty/whitespace
    /// values are ignored — registering them would redact everything.</summary>
    public void Register(string? secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
        {
            _secrets[secret] = 0;
        }
    }

    /// <summary>Replace every registered secret in <paramref name="message"/> with
    /// <see cref="Placeholder"/>. Returns the message unchanged when nothing matches.</summary>
    public string Redact(string message)
    {
        if (string.IsNullOrEmpty(message) || _secrets.IsEmpty)
        {
            return message;
        }

        string result = message;
        foreach (string secret in _secrets.Keys)
        {
            if (result.Contains(secret, StringComparison.Ordinal))
            {
                result = result.Replace(secret, Placeholder, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
