using Microsoft.Extensions.Logging;

namespace Lore.Agent.Config;

/// <summary>An <see cref="ILogger"/> decorator that scrubs registered secrets from each
/// formatted message before passing it to the inner logger (constitution §4.2). Wrap a
/// real sink with <see cref="RedactingLoggerProvider"/> so nothing — not even an accidental
/// interpolation of an API key — can reach <c>lore.log</c> in the clear.</summary>
public sealed class RedactingLogger : ILogger
{
    private readonly ILogger _inner;
    private readonly SecretRegistry _registry;

    public RedactingLogger(ILogger inner, SecretRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        _inner = inner;
        _registry = registry;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => _inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        // The message is materialized once, here, then redacted; the inner logger only
        // ever sees the scrubbed text.
        string Redacting(TState s, Exception? e) => _registry.Redact(formatter(s, e));
        _inner.Log(logLevel, eventId, state, exception, Redacting);
    }
}

/// <summary>Wraps another <see cref="ILoggerProvider"/> so every logger it hands out
/// redacts secrets. Register the real sink (console, file) decorated by this.</summary>
public sealed class RedactingLoggerProvider : ILoggerProvider
{
    private readonly ILoggerProvider _inner;
    private readonly SecretRegistry _registry;

    public RedactingLoggerProvider(ILoggerProvider inner, SecretRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(registry);
        _inner = inner;
        _registry = registry;
    }

    public ILogger CreateLogger(string categoryName) =>
        new RedactingLogger(_inner.CreateLogger(categoryName), _registry);

    public void Dispose() => _inner.Dispose();
}
