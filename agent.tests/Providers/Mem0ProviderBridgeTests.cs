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
    public void Gemini_gets_its_own_first_party_embedder()
    {
        var resolved = new ResolvedProvider(ProviderKind.Gemini, "gemini-2.5-flash", null, "lore/provider", 256);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "g-key", DataDir);

        // The embedder rides the same Gemini OpenAI-compatible endpoint, so one key drives
        // both LLM and embeddings — no local Ollama required (spec 013).
        Assert.NotNull(config.Embedder);
        Assert.Equal("openai", config.Embedder!.Type);
        Assert.Equal("text-embedding-004", config.Embedder.Model);
        Assert.Equal(
            new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"), config.Embedder.BaseUrl);
        Assert.Equal(768, config.Embedder.Dims);
        Assert.Null(config.Embedder.ApiKey); // reuses the provider key (same host)
    }

    [Fact]
    public void An_explicit_embedder_overrides_the_provider_default()
    {
        var resolved = new ResolvedProvider(ProviderKind.Gemini, "gemini-2.5-flash", null, "lore/provider", 256);
        var explicitEmbedder = new MemoryEmbedderConfig(
            "ollama", "nomic-embed-text", new Uri("http://localhost:11434"), 768);

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "g-key", DataDir, explicitEmbedder);

        Assert.Same(explicitEmbedder, config.Embedder); // explicit wins over the Gemini default
    }

    [Fact]
    public void An_explicit_embedder_makes_a_no_embeddings_provider_work()
    {
        // Anthropic has no embeddings; an explicit embedder (its own key) is the supported path.
        var resolved = new ResolvedProvider(ProviderKind.Anthropic, "claude-haiku-4-5", null, "lore/provider", 256);
        var explicitEmbedder = new MemoryEmbedderConfig(
            "openai", "text-embedding-3-small", null, 1536, ApiKey: "sk-embedder");

        MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, "anthropic-key", DataDir, explicitEmbedder);

        Assert.Equal("anthropic", config.Provider.Type);
        Assert.NotNull(config.Embedder);
        Assert.Equal("text-embedding-3-small", config.Embedder!.Model);
        Assert.Equal("sk-embedder", config.Embedder.ApiKey); // separate key honored
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
