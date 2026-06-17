using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class ProviderTesterTests
{
    private static ProviderOptions ValidOptions() =>
        new() { Type = "openai", Model = "gpt-4o-mini", ApiKeyRef = "lore/provider" };

    [Fact]
    public async Task Success_reports_the_model_and_a_latency()
    {
        var factory = new FakeBackendFactory(_ => StubBackend.Returns("OK"));
        var tester = new ProviderTester(factory);

        ProviderTestResult result = await tester.TestAsync(ValidOptions());

        Assert.True(result.Ok);
        Assert.Equal("gpt-4o-mini", result.Model);
        Assert.NotNull(result.LatencyMs);
        Assert.True(result.LatencyMs >= 0);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Succeeds_even_when_the_model_returns_no_text()
    {
        // Connectivity + auth are what we test; an empty completion still proves both.
        var tester = new ProviderTester(new FakeBackendFactory(_ => StubBackend.Returns(null)));

        ProviderTestResult result = await tester.TestAsync(ValidOptions());

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task Caps_the_probe_max_tokens()
    {
        var factory = new FakeBackendFactory(_ => StubBackend.Returns("OK"));
        var tester = new ProviderTester(factory);

        await tester.TestAsync(new ProviderOptions
        {
            Type = "openai",
            Model = "m",
            ApiKeyRef = "lore/provider",
            MaxTokens = 4096,
        });

        Assert.NotNull(factory.LastProvider);
        Assert.True(factory.LastProvider!.MaxTokens <= 16);
    }

    [Theory]
    [InlineData(ProviderErrorKind.Unauthorized)]
    [InlineData(ProviderErrorKind.Unreachable)]
    [InlineData(ProviderErrorKind.ModelNotFound)]
    public async Task Maps_a_provider_exception_to_its_kind_and_message(ProviderErrorKind kind)
    {
        var factory = new FakeBackendFactory(
            _ => StubBackend.Throws(new ProviderException(kind, "actionable detail")));
        var tester = new ProviderTester(factory);

        ProviderTestResult result = await tester.TestAsync(ValidOptions());

        Assert.False(result.Ok);
        Assert.Equal(kind.ToString(), result.ErrorKind);
        Assert.Equal("actionable detail", result.Error);
        Assert.Null(result.Model);
        Assert.Null(result.LatencyMs);
    }

    [Fact]
    public async Task Invalid_config_fails_before_any_call()
    {
        var factory = new FakeBackendFactory(_ => StubBackend.Returns("OK"));
        var tester = new ProviderTester(factory);

        // Missing model — the selector rejects it.
        ProviderTestResult result = await tester.TestAsync(new ProviderOptions { Type = "openai" });

        Assert.False(result.Ok);
        Assert.Equal("configuration", result.ErrorKind);
        Assert.Null(factory.LastProvider); // never reached the factory
    }

    [Fact]
    public async Task Missing_stored_key_is_a_configuration_failure()
    {
        var factory = new FakeBackendFactory(
            _ => throw new ProviderConfigurationException("No API key is stored under handle 'lore/provider'."));
        var tester = new ProviderTester(factory);

        ProviderTestResult result = await tester.TestAsync(ValidOptions());

        Assert.False(result.Ok);
        Assert.Equal("configuration", result.ErrorKind);
        Assert.Contains("api key", result.Error!, StringComparison.OrdinalIgnoreCase);
    }
}
