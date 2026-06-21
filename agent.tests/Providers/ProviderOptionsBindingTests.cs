using System.IO;
using System.Net.Http;
using System.Text.Json;
using Lore.Agent.Api.Endpoints;
using Lore.Agent.Hosting;
using Lore.Agent.Providers;
using Lore.Agent.Tests.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Providers;

/// <summary>Launch-blocker regression: config.json stores the provider block in snake_case
/// (<c>api_key_ref</c>, <c>base_url</c>) — what onboarding and PATCH /config write through
/// <c>LoreConfig</c>. The agent binds it with
/// <c>configuration.GetSection("provider").Get&lt;ProviderOptions&gt;()</c> (the same path
/// <c>Program.cs</c> takes after <c>AddJsonFile</c>). The binder is case-insensitive but does
/// NOT strip underscores, so without <c>[ConfigurationKeyName]</c> the snake_case keys never
/// bind, <see cref="ProviderSelector"/> throws, and capture silently falls back to the
/// <c>NullInferenceBackend</c> — no memories are ever produced. The existing tests construct
/// <see cref="ProviderOptions"/> directly in PascalCase and never exercise this JSON path, so
/// they missed it. These tests bind a real snake_case document end to end.</summary>
public sealed class ProviderOptionsBindingTests
{
    private static ProviderOptions BindFromJson(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"lore-provider-bind-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            IConfiguration configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: false, reloadOnChange: false)
                .Build();
            return configuration.GetSection("provider").Get<ProviderOptions>() ?? new ProviderOptions();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Binds_snake_case_cloud_provider_block()
    {
        ProviderOptions options = BindFromJson(
            """
            {
              "provider": {
                "type": "gemini",
                "model": "gemini-2.0-flash",
                "api_key_ref": "lore/provider",
                "max_tokens": 512
              }
            }
            """);

        Assert.Equal("gemini", options.Type);
        Assert.Equal("gemini-2.0-flash", options.Model);
        Assert.Equal("lore/provider", options.ApiKeyRef); // would be null without [ConfigurationKeyName]
        Assert.Equal(512, options.MaxTokens);

        // The binding has to actually satisfy the selector, not merely populate fields.
        ResolvedProvider resolved = ProviderSelector.Resolve(options);
        Assert.Equal(ProviderKind.Gemini, resolved.Kind);
        Assert.Equal("lore/provider", resolved.ApiKeyRef);
    }

    [Fact]
    public void Binds_snake_case_keyless_openai_compatible_block()
    {
        ProviderOptions options = BindFromJson(
            """
            {
              "provider": {
                "type": "openai_compatible",
                "model": "qwen3:8b",
                "base_url": "http://localhost:11434/v1",
                "api_key_ref": ""
              }
            }
            """);

        Assert.Equal("http://localhost:11434/v1", options.BaseUrl); // would be null without [ConfigurationKeyName]

        ResolvedProvider resolved = ProviderSelector.Resolve(options);
        Assert.Equal(ProviderKind.OpenAiCompatible, resolved.Kind);
        Assert.Equal(new Uri("http://localhost:11434/v1"), resolved.BaseUrl);
    }

    [Fact]
    public async Task Status_reports_ready_for_a_snake_case_gemini_config()
    {
        ProviderOptions options = BindFromJson(
            """
            {
              "provider": {
                "type": "gemini",
                "model": "gemini-2.0-flash",
                "api_key_ref": "lore/provider"
              }
            }
            """);

        await using LoreApiHarness harness = await LoreApiHarness.StartAsync(
            services =>
            {
                services.AddSingleton<IMemorydReadiness>(new StubMemorydReadiness(ready: true));
                services.AddSingleton(options);
            },
            app => app.MapSystemEndpoints());

        HttpResponseMessage response = await harness.Client.GetAsync(new Uri("/system/status", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal("ready", doc.RootElement.GetProperty("components").GetProperty("provider").GetString());
    }
}
