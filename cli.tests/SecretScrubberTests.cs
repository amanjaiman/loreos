using System.Text.Json.Nodes;

namespace Lore.Cli.Tests;

/// <summary>Defense-in-depth redaction (spec 007 non-functional: never print secret material).
/// Secret-looking values are replaced at any depth; <c>*_ref</c> handles and ordinary fields are
/// left intact.</summary>
public sealed class SecretScrubberTests
{
    [Theory]
    [InlineData("api_key")]
    [InlineData("apiKey")]
    [InlineData("password")]
    [InlineData("provider_password")]
    [InlineData("access_token")]
    [InlineData("client_secret")]
    [InlineData("Authorization")]
    public void Redacts_secret_keys(string key)
    {
        var node = new JsonObject { [key] = "super-secret" };

        SecretScrubber.Scrub(node);

        Assert.Equal(SecretScrubber.Placeholder, node[key]!.GetValue<string>());
    }

    [Theory]
    [InlineData("api_key_ref")]
    [InlineData("model")]
    [InlineData("base_url")]
    [InlineData("id")]
    public void Keeps_non_secret_keys(string key)
    {
        var node = new JsonObject { [key] = "value" };

        SecretScrubber.Scrub(node);

        Assert.Equal("value", node[key]!.GetValue<string>());
    }

    [Fact]
    public void Redacts_nested_objects_and_arrays()
    {
        var node = new JsonObject
        {
            ["provider"] = new JsonObject
            {
                ["model"] = "gpt-4o",
                ["api_key"] = "sk-aaa",
            },
            ["accounts"] = new JsonArray(
                new JsonObject { ["token"] = "t-1" },
                new JsonObject { ["token"] = "t-2" }),
        };

        SecretScrubber.Scrub(node);

        Assert.Equal("gpt-4o", node["provider"]!["model"]!.GetValue<string>());
        Assert.Equal(SecretScrubber.Placeholder, node["provider"]!["api_key"]!.GetValue<string>());
        Assert.Equal(SecretScrubber.Placeholder, node["accounts"]![0]!["token"]!.GetValue<string>());
        Assert.Equal(SecretScrubber.Placeholder, node["accounts"]![1]!["token"]!.GetValue<string>());
    }

    [Fact]
    public void Tolerates_null()
    {
        SecretScrubber.Scrub(null); // must not throw
    }
}
