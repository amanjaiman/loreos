using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class ProviderSelectorTests
{
    [Theory]
    [InlineData("anthropic", ProviderKind.Anthropic)]
    [InlineData("openai", ProviderKind.OpenAi)]
    [InlineData("gemini", ProviderKind.Gemini)]
    [InlineData("OpenAI", ProviderKind.OpenAi)]
    [InlineData("  Anthropic  ", ProviderKind.Anthropic)]
    public void Resolves_the_kind_from_type_case_insensitively(string type, ProviderKind expected)
    {
        var options = new ProviderOptions { Type = type, Model = "m", ApiKeyRef = "lore/provider" };

        ResolvedProvider resolved = ProviderSelector.Resolve(options);

        Assert.Equal(expected, resolved.Kind);
        Assert.Equal("m", resolved.Model);
    }

    [Fact]
    public void Resolves_openai_compatible_with_base_url_and_optional_key()
    {
        var options = new ProviderOptions
        {
            Type = "openai_compatible",
            Model = "qwen3:8b",
            BaseUrl = "http://localhost:11434/v1",
        };

        ResolvedProvider resolved = ProviderSelector.Resolve(options);

        Assert.Equal(ProviderKind.OpenAiCompatible, resolved.Kind);
        Assert.Equal(new Uri("http://localhost:11434/v1"), resolved.BaseUrl);
        Assert.Null(resolved.ApiKeyRef); // keyless local endpoint is allowed
    }

    [Fact]
    public void Trims_model_and_handle()
    {
        var options = new ProviderOptions { Type = "openai", Model = " gpt-4o ", ApiKeyRef = " lore/k " };

        ResolvedProvider resolved = ProviderSelector.Resolve(options);

        Assert.Equal("gpt-4o", resolved.Model);
        Assert.Equal("lore/k", resolved.ApiKeyRef);
    }

    [Fact]
    public void Ignores_base_url_for_first_party_providers()
    {
        var options = new ProviderOptions
        {
            Type = "openai",
            Model = "gpt-4o",
            ApiKeyRef = "lore/provider",
            BaseUrl = "http://example.invalid",
        };

        ResolvedProvider resolved = ProviderSelector.Resolve(options);

        Assert.Null(resolved.BaseUrl);
    }

    [Fact]
    public void Empty_type_yields_a_configure_a_provider_error()
    {
        var options = new ProviderOptions { Type = "", Model = "m" };

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
        Assert.Contains("provider.type", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_type_lists_the_valid_types()
    {
        var options = new ProviderOptions { Type = "cohere", Model = "m" };

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
        Assert.Contains("openai_compatible", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_model_is_rejected()
    {
        var options = new ProviderOptions { Type = "anthropic", Model = "  ", ApiKeyRef = "lore/k" };

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
        Assert.Contains("provider.model", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Openai_compatible_without_base_url_is_rejected()
    {
        var options = new ProviderOptions { Type = "openai_compatible", Model = "qwen3:8b" };

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
        Assert.Contains("provider.base_url", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://localhost/v1")]
    [InlineData("localhost:11434")]
    public void Openai_compatible_with_a_bad_base_url_is_rejected(string raw)
    {
        var options = new ProviderOptions { Type = "openai_compatible", Model = "m", BaseUrl = raw };

        Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
    }

    [Theory]
    [InlineData("anthropic")]
    [InlineData("openai")]
    [InlineData("gemini")]
    public void Cloud_providers_require_an_api_key_ref(string type)
    {
        var options = new ProviderOptions { Type = type, Model = "m", ApiKeyRef = null };

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
        Assert.Contains("provider.api_key_ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_positive_max_tokens_is_rejected()
    {
        var options = new ProviderOptions
        {
            Type = "openai",
            Model = "m",
            ApiKeyRef = "lore/k",
            MaxTokens = 0,
        };

        Assert.Throws<ProviderConfigurationException>(() => ProviderSelector.Resolve(options));
    }

    [Fact]
    public void Null_options_throws_argument_null()
    {
        Assert.Throws<ArgumentNullException>(() => ProviderSelector.Resolve(null!));
    }
}
