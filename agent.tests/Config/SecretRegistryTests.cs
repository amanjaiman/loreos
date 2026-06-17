using Lore.Agent.Config;

namespace Lore.Agent.Tests.Config;

public sealed class SecretRegistryTests
{
    [Fact]
    public void Redacts_a_registered_secret_anywhere_in_the_message()
    {
        var registry = new SecretRegistry();
        registry.Register("sk-abc123");

        string result = registry.Redact("calling model with key sk-abc123 now");

        Assert.Equal($"calling model with key {SecretRegistry.Placeholder} now", result);
    }

    [Fact]
    public void Redacts_every_registered_secret()
    {
        var registry = new SecretRegistry();
        registry.Register("key-one");
        registry.Register("key-two");

        Assert.Equal(
            $"{SecretRegistry.Placeholder} and {SecretRegistry.Placeholder}",
            registry.Redact("key-one and key-two"));
    }

    [Fact]
    public void Leaves_messages_without_secrets_unchanged()
    {
        var registry = new SecretRegistry();
        registry.Register("secret");

        Assert.Equal("nothing to see here", registry.Redact("nothing to see here"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Ignores_blank_secrets_so_redaction_does_not_swallow_everything(string? blank)
    {
        var registry = new SecretRegistry();
        registry.Register(blank);

        Assert.Equal("untouched message", registry.Redact("untouched message"));
    }
}
