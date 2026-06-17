using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Providers;

namespace Lore.Agent.Tests.Api;

public sealed class ProviderEndpointsTests
{
    private static readonly JsonSerializerOptions OmitNulls =
        new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    [Fact]
    public void Request_merge_overlays_set_fields_onto_current()
    {
        var current = new ProviderOptions
        {
            Type = "openai",
            Model = "gpt-4o",
            ApiKeyRef = "lore/provider",
            MaxTokens = 512,
        };
        var request = new ProviderTestRequest { Model = "gpt-4o-mini" };

        ProviderOptions merged = request.Merge(current);

        Assert.Equal("openai", merged.Type);          // kept from current
        Assert.Equal("gpt-4o-mini", merged.Model);     // overridden
        Assert.Equal("lore/provider", merged.ApiKeyRef);
        Assert.Equal(512, merged.MaxTokens);
    }

    [Fact]
    public async Task Handle_uses_the_current_config_when_no_body_is_supplied()
    {
        var factory = new FakeBackendFactory(_ => StubBackend.Returns("OK"));
        var tester = new ProviderTester(factory);
        var current = new ProviderOptions { Type = "anthropic", Model = "claude", ApiKeyRef = "lore/provider" };

        ProviderTestResult result = await ProviderEndpoints.HandleAsync(null, current, tester);

        Assert.True(result.Ok);
        Assert.Equal("claude", result.Model);
    }

    [Fact]
    public async Task Handle_tests_the_supplied_config()
    {
        var factory = new FakeBackendFactory(_ => StubBackend.Returns("OK"));
        var tester = new ProviderTester(factory);
        var current = new ProviderOptions { Type = "anthropic", Model = "claude", ApiKeyRef = "lore/provider" };
        var request = new ProviderTestRequest { Type = "openai", Model = "gpt-4o", ApiKeyRef = "lore/openai" };

        ProviderTestResult result = await ProviderEndpoints.HandleAsync(request, current, tester);

        Assert.True(result.Ok);
        Assert.Equal("gpt-4o", result.Model);
    }

    [Fact]
    public void Success_serializes_to_ok_model_latency_only()
    {
        string json = JsonSerializer.Serialize(ProviderTestResult.Success("gpt-4o", 42), OmitNulls);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("gpt-4o", root.GetProperty("model").GetString());
        Assert.Equal(42, root.GetProperty("latency_ms").GetInt64());
        Assert.False(root.TryGetProperty("error", out _));
        Assert.False(root.TryGetProperty("error_kind", out _));
    }

    [Fact]
    public void Failure_serializes_to_ok_false_with_error_and_kind()
    {
        string json = JsonSerializer.Serialize(
            ProviderTestResult.Fail("Unauthorized", "bad key"), OmitNulls);

        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("bad key", root.GetProperty("error").GetString());
        Assert.Equal("Unauthorized", root.GetProperty("error_kind").GetString());
        Assert.False(root.TryGetProperty("model", out _));
        Assert.False(root.TryGetProperty("latency_ms", out _));
    }
}
