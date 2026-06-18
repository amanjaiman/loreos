using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 T006: 004's test-connection is reachable through the API. (The <c>/import</c>
/// route, reserved here as a 501 placeholder, is now implemented and covered by
/// <see cref="ImportEndpointsTests"/> for spec 009 T004.)</summary>
public sealed class ProviderImportEndpointsTests
{
    [Fact]
    public async Task Providers_test_runs_through_the_api_against_the_current_config()
    {
        var current = new ProviderOptions { Type = "anthropic", Model = "claude-haiku-4-5", ApiKeyRef = "lore/provider" };
        var tester = new ProviderTester(new FakeBackendFactory(_ => StubBackend.Returns("OK")));
        await using LoreApiHarness harness = await LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton(current);
                services.AddSingleton(tester);
            },
            app => app.MapProviderEndpoints());

        HttpResponseMessage response = await harness.Client.PostAsJsonAsync(
            new Uri("/providers/test", UriKind.Relative), new { model = "claude-haiku-4-5" });

        response.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("claude-haiku-4-5", doc.RootElement.GetProperty("model").GetString());
    }
}
