using System.Collections.Concurrent;
using Lore.Agent.Config;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Tests.Config;

/// <summary>A non-Windows <see cref="ICredentialStore"/> for exercising callers (the
/// migration, the provider factory) without touching the real Credential Manager.</summary>
internal sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _store = new(StringComparer.Ordinal);

    public string? Read(string handle) => _store.TryGetValue(handle, out string? value) ? value : null;

    public void Write(string handle, string secret) => _store[handle] = secret;

    public void Delete(string handle) => _store.TryRemove(handle, out _);
}

/// <summary>An <see cref="ILogger"/> that records each formatted message, to prove what a
/// decorator (the redactor) hands to the underlying sink.</summary>
internal sealed class CapturingLogger : ILogger
{
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
}
