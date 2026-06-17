using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Api;

/// <summary>Spec 005 T006: 004's test-connection is reachable through the API, and the reserved
/// <c>POST /import</c> exists in the contract as a 501 placeholder for 009. Exercised end-to-end
/// over a live loopback host.</summary>
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

    [Fact]
    public async Task Import_is_reserved_and_answers_501()
    {
        await using LoreApiHarness harness = await LoreApiHarness.StartAsync(
            _ => { },
            app => app.MapImportEndpoints());

        HttpResponseMessage response = await harness.Client.PostAsync(
            new Uri("/import", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("009", doc.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
    }
}
