namespace Lore.Agent.Config;

/// <summary>The one seam to OS secret storage (spec 004). Backends resolve an
/// <c>api_key_ref</c> handle to the actual key at call time; the key never lives in
/// <c>config.json</c> and is never logged (constitution §4.2). On Windows this is the
/// Credential Manager (DPAPI); the interface keeps that platform detail behind a testable
/// boundary so a bad-keystore case can fail closed with guidance.</summary>
public interface ICredentialStore
{
    /// <summary>Return the secret stored under <paramref name="handle"/>, or <c>null</c>
    /// if there is none.</summary>
    string? Read(string handle);

    /// <summary>Store (or overwrite) the secret under <paramref name="handle"/>.</summary>
    void Write(string handle, string secret);

    /// <summary>Remove the secret under <paramref name="handle"/>. A no-op if absent.</summary>
    void Delete(string handle);
}
