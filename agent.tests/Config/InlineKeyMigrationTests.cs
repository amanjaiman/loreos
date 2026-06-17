using System.IO;
using System.Text.Json;
using Lore.Agent.Config;

namespace Lore.Agent.Tests.Config;

public sealed class InlineKeyMigrationTests
{
    [Fact]
    public void Moves_an_inline_key_into_the_store_and_strips_it_from_config()
    {
        var store = new InMemoryCredentialStore();
        const string json =
            """{"provider":{"type":"openai","model":"gpt-4o","api_key":"sk-secret"}}""";

        bool migrated = InlineKeyMigration.TryMigrate(json, store, out string result);

        Assert.True(migrated);
        Assert.Equal("sk-secret", store.Read(InlineKeyMigration.DefaultHandle));

        using JsonDocument doc = JsonDocument.Parse(result);
        JsonElement provider = doc.RootElement.GetProperty("provider");
        Assert.False(provider.TryGetProperty("api_key", out _)); // no key material remains
        Assert.Equal(InlineKeyMigration.DefaultHandle, provider.GetProperty("api_key_ref").GetString());
        Assert.DoesNotContain("sk-secret", result, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_the_key_under_an_existing_handle_when_one_is_named()
    {
        var store = new InMemoryCredentialStore();
        const string json =
            """{"provider":{"type":"anthropic","model":"c","api_key":"k","api_key_ref":"lore/custom"}}""";

        Assert.True(InlineKeyMigration.TryMigrate(json, store, out string result));
        Assert.Equal("k", store.Read("lore/custom"));
        Assert.Null(store.Read(InlineKeyMigration.DefaultHandle));

        using JsonDocument doc = JsonDocument.Parse(result);
        Assert.Equal("lore/custom", doc.RootElement.GetProperty("provider").GetProperty("api_key_ref").GetString());
    }

    [Theory]
    [InlineData("""{"provider":{"type":"openai_compatible","model":"m","api_key_ref":"lore/provider"}}""")]
    [InlineData("""{"provider":{"type":"openai","model":"m"}}""")]
    [InlineData("""{"provider":{"type":"openai","model":"m","api_key":"   "}}""")]
    [InlineData("""{"memory":{"engine":"embedded"}}""")]
    public void Does_nothing_when_there_is_no_inline_key(string json)
    {
        var store = new InMemoryCredentialStore();

        bool migrated = InlineKeyMigration.TryMigrate(json, store, out string result);

        Assert.False(migrated);
        Assert.Equal(json, result);
    }

    [Fact]
    public void Run_rewrites_the_file_in_place()
    {
        var store = new InMemoryCredentialStore();
        string path = Path.Combine(Path.GetTempPath(), $"lore-cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"provider":{"type":"openai","model":"gpt-4o","api_key":"sk-file"}}""");
        try
        {
            Assert.True(InlineKeyMigration.Run(path, store));

            string rewritten = File.ReadAllText(path);
            Assert.DoesNotContain("sk-file", rewritten, StringComparison.Ordinal);
            Assert.Contains("api_key_ref", rewritten, StringComparison.Ordinal);
            Assert.Equal("sk-file", store.Read(InlineKeyMigration.DefaultHandle));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Run_returns_false_when_the_file_is_absent()
    {
        var store = new InMemoryCredentialStore();
        string path = Path.Combine(Path.GetTempPath(), $"lore-missing-{Guid.NewGuid():N}.json");

        Assert.False(InlineKeyMigration.Run(path, store));
    }
}
