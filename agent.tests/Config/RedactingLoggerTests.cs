using Lore.Agent.Config;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Tests.Config;

public sealed class RedactingLoggerTests
{
    [Fact]
    public void Scrubs_registered_secrets_before_they_reach_the_inner_sink()
    {
        var registry = new SecretRegistry();
        registry.Register("sk-live-9999");
        var inner = new CapturingLogger();
        var logger = new RedactingLogger(inner, registry);

        // state is the message; the formatter returns it verbatim — the redactor is the
        // only thing that may alter it.
        logger.Log(LogLevel.Information, default, "provider key = sk-live-9999", null, (state, _) => state);

        Assert.Equal($"provider key = {SecretRegistry.Placeholder}", Assert.Single(inner.Messages));
    }

    [Fact]
    public void Passes_clean_messages_through_unchanged()
    {
        var registry = new SecretRegistry();
        var inner = new CapturingLogger();
        var logger = new RedactingLogger(inner, registry);

        logger.Log(LogLevel.Warning, default, "memoryd restarted", null, (state, _) => state);

        Assert.Equal("memoryd restarted", Assert.Single(inner.Messages));
    }

    [Fact]
    public void Provider_decorates_loggers_it_creates()
    {
        var registry = new SecretRegistry();
        registry.Register("topsecret");
        var inner = new CapturingLogger();
        using var innerProvider = new SingleLoggerProvider(inner);
        using var provider = new RedactingLoggerProvider(innerProvider, registry);

        ILogger logger = provider.CreateLogger("cat");
        logger.Log(LogLevel.Information, default, "value=topsecret", null, (state, _) => state);

        Assert.Equal($"value={SecretRegistry.Placeholder}", Assert.Single(inner.Messages));
    }

    private sealed class SingleLoggerProvider : ILoggerProvider
    {
        private readonly ILogger _logger;

        public SingleLoggerProvider(ILogger logger) => _logger = logger;

        public ILogger CreateLogger(string categoryName) => _logger;

        public void Dispose()
        {
        }
    }
}
