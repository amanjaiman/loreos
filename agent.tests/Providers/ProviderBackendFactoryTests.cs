using System.Net;
using System.Net.Http;
using Lore.Agent.Inference;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Config;

namespace Lore.Agent.Tests.Providers;

public sealed class ProviderBackendFactoryTests : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }
    }

    private (ProviderBackendFactory Factory, CapturingHttpHandler Handler, InMemoryCredentialStore Store) Build()
    {
        var handler = new CapturingHttpHandler(
            HttpStatusCode.OK, """{"choices":[{"message":{"content":"ok"}}]}""");
        var http = new HttpClient(handler);
        _clients.Add(http);
        var store = new InMemoryCredentialStore();
        return (new ProviderBackendFactory(new StubHttpClientFactory(http), store), handler, store);
    }

    [Theory]
    [InlineData(ProviderKind.Anthropic, typeof(AnthropicBackend))]
    [InlineData(ProviderKind.OpenAi, typeof(OpenAiBackend))]
    [InlineData(ProviderKind.Gemini, typeof(GeminiBackend))]
    public void Builds_the_backend_for_each_cloud_kind(ProviderKind kind, Type expected)
    {
        (ProviderBackendFactory factory, _, InMemoryCredentialStore store) = Build();
        store.Write("lore/provider", "the-key");
        var resolved = new ResolvedProvider(kind, "model", null, "lore/provider", 256);

        IInferenceBackend backend = factory.Create(resolved);

        Assert.IsType(expected, backend);
    }

    [Fact]
    public void Builds_a_keyless_openai_compatible_backend()
    {
        (ProviderBackendFactory factory, _, _) = Build();
        var resolved = new ResolvedProvider(
            ProviderKind.OpenAiCompatible, "qwen3:8b", new Uri("http://localhost:11434/v1"), null, 256);

        IInferenceBackend backend = factory.Create(resolved);

        Assert.IsType<OpenAiCompatibleBackend>(backend);
    }

    [Fact]
    public async Task Resolves_the_key_from_the_store_and_passes_it_to_the_backend()
    {
        (ProviderBackendFactory factory, CapturingHttpHandler handler, InMemoryCredentialStore store) = Build();
        store.Write("lore/provider", "sk-from-store");
        var resolved = new ResolvedProvider(ProviderKind.OpenAi, "gpt-4o", null, "lore/provider", 256);

        IInferenceBackend backend = factory.Create(resolved);
        await backend.CompleteAsync(new InferenceRequest("s", "u"));

        Assert.Equal("sk-from-store", handler.Request!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public void Missing_stored_key_throws_a_configuration_error()
    {
        (ProviderBackendFactory factory, _, _) = Build(); // store is empty
        var resolved = new ResolvedProvider(ProviderKind.Anthropic, "claude", null, "lore/provider", 256);

        ProviderConfigurationException ex =
            Assert.Throws<ProviderConfigurationException>(() => factory.Create(resolved));
        Assert.Contains("lore/provider", ex.Message, StringComparison.Ordinal);
    }
}
