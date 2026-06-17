using System.Net;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Config;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Tests.Providers;

public sealed class Mem0ConfiguratorTests : IDisposable
{
    private readonly List<HttpClient> _clients = [];

    public void Dispose()
    {
        foreach (HttpClient client in _clients)
        {
            client.Dispose();
        }
    }

    private (MemorydClient Client, StubHttpMessageHandler Handler) Memoryd()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, "{}"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:7843/") };
        _clients.Add(http);
        return (new MemorydClient(http), handler);
    }

    private static Mem0Configurator Build(
        MemorydClient client, ProviderOptions options, ICredentialStore store) =>
        new(
            new ImmediateReadiness(),
            client,
            options,
            store,
            Options.Create(new MemorydOptions { DataDir = @"C:\Lore\data" }),
            NullLogger<Mem0Configurator>.Instance);

    [Fact]
    public async Task Posts_the_bridged_provider_config_to_memoryd()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Memoryd();
        var store = new InMemoryCredentialStore();
        store.Write("lore/provider", "sk-from-store");
        var options = new ProviderOptions { Type = "openai", Model = "gpt-4o", ApiKeyRef = "lore/provider" };

        using Mem0Configurator configurator = Build(client, options, store);
        await configurator.ConfigureAsync(default);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith("config", handler.LastUri!.ToString(), StringComparison.Ordinal);

        using JsonDocument body = JsonDocument.Parse(handler.LastBody!);
        JsonElement provider = body.RootElement.GetProperty("provider");
        Assert.Equal("openai", provider.GetProperty("type").GetString());
        Assert.Equal("gpt-4o", provider.GetProperty("model").GetString());
        Assert.Equal("sk-from-store", provider.GetProperty("api_key").GetString());
        Assert.Equal(@"C:\Lore\data", body.RootElement.GetProperty("data_dir").GetString());
    }

    [Fact]
    public async Task Keyless_local_provider_posts_an_ollama_config_without_a_key()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Memoryd();
        var options = new ProviderOptions
        {
            Type = "openai_compatible",
            Model = "qwen3:8b",
            BaseUrl = "http://localhost:11434/v1",
        };

        using Mem0Configurator configurator = Build(client, options, new InMemoryCredentialStore());
        await configurator.ConfigureAsync(default);

        using JsonDocument body = JsonDocument.Parse(handler.LastBody!);
        JsonElement provider = body.RootElement.GetProperty("provider");
        Assert.Equal("ollama", provider.GetProperty("type").GetString());
        Assert.False(provider.TryGetProperty("api_key", out _)); // null key omitted
    }

    [Fact]
    public async Task Invalid_config_makes_no_call_and_does_not_throw()
    {
        (MemorydClient client, StubHttpMessageHandler handler) = Memoryd();
        var options = new ProviderOptions { Type = "openai" }; // no model -> selector rejects

        using Mem0Configurator configurator = Build(client, options, new InMemoryCredentialStore());
        await configurator.ConfigureAsync(default);

        Assert.Null(handler.LastBody); // memoryd was never called
    }

    private sealed class ImmediateReadiness : IReadinessSignal
    {
        public Task WaitUntilReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
