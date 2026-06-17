using Lore.Agent.Memory;
using Lore.Agent.Providers;

namespace Lore.Agent.Tests.Providers;

public sealed class Mem0ProviderBridgeTests
{
    private const string DataDir = @"C:\Users\me\AppData\Local\Lore";

    [Fact]
    public void Anthropic_maps_to_anthropic_with_the_key_and_no_base_url()
    {
        var resolved = new ResolvedProvider(ProviderKind.Anthropic, "claude-haiku-4-5", null, "lore/provider", 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "the-key", DataDir);

        Assert.Equal("anthropic", config.Provider.Type);
        Assert.Equal("claude-haiku-4-5", config.Provider.Model);
        Assert.Equal("the-key", config.Provider.ApiKey);
        Assert.Null(config.Provider.BaseUrl);
        Assert.Equal(DataDir, config.DataDir);
        Assert.Null(config.Embedder); // follow_provider default at memoryd
        Assert.Equal("lore", config.CollectionName);
    }

    [Fact]
    public void OpenAi_maps_to_openai_with_the_key()
    {
        var resolved = new ResolvedProvider(ProviderKind.OpenAi, "gpt-4o-mini", null, "lore/provider", 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "sk-key", DataDir);

        Assert.Equal("openai", config.Provider.Type);
        Assert.Equal("sk-key", config.Provider.ApiKey);
        Assert.Null(config.Provider.BaseUrl);
    }

    [Fact]
    public void Gemini_is_routed_through_the_openai_compatible_endpoint()
    {
        var resolved = new ResolvedProvider(ProviderKind.Gemini, "gemini-2.5-flash", null, "lore/provider", 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "g-key", DataDir);

        Assert.Equal("openai_compatible", config.Provider.Type);
        Assert.Equal("g-key", config.Provider.ApiKey);
        Assert.Equal(
            new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"), config.Provider.BaseUrl);
    }

    [Fact]
    public void Keyed_openai_compatible_keeps_its_base_url()
    {
        var resolved = new ResolvedProvider(
            ProviderKind.OpenAiCompatible, "some/model", new Uri("https://openrouter.ai/api/v1"), "lore/provider", 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "or-key", DataDir);

        Assert.Equal("openai_compatible", config.Provider.Type);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1"), config.Provider.BaseUrl);
        Assert.Equal("or-key", config.Provider.ApiKey);
    }

    [Fact]
    public void Keyless_openai_compatible_maps_to_ollama_with_the_native_base_url()
    {
        var resolved = new ResolvedProvider(
            ProviderKind.OpenAiCompatible, "qwen3:8b", new Uri("http://localhost:11434/v1"), null, 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, null, DataDir);

        Assert.Equal("ollama", config.Provider.Type);
        Assert.Equal(new Uri("http://localhost:11434"), config.Provider.BaseUrl); // /v1 stripped
        Assert.Null(config.Provider.ApiKey);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/")]
    [InlineData("http://localhost:11434/v1/", "http://localhost:11434/")]
    [InlineData("http://host:1234", "http://host:1234/")]
    public void Strips_the_openai_v1_suffix_for_ollama(string raw, string expected)
    {
        var resolved = new ResolvedProvider(
            ProviderKind.OpenAiCompatible, "m", new Uri(raw), null, 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, null, DataDir);

        Assert.Equal(new Uri(expected), config.Provider.BaseUrl);
    }

    [Fact]
    public void Null_provider_throws()
    {
        Assert.Throws<ArgumentNullException>(() => Mem0ProviderBridge.ToMemoryConfig(null!, "k", DataDir));
    }

    [Fact]
    public void Blank_data_dir_throws()
    {
        var resolved = new ResolvedProvider(ProviderKind.OpenAi, "m", null, "lore/provider", 256);

        Assert.Throws<ArgumentException>(() => Mem0ProviderBridge.ToMemoryConfig(resolved, "k", "  "));
    }
}
