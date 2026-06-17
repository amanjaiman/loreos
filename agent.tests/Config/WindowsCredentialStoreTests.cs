using Lore.Agent.Config;

namespace Lore.Agent.Tests.Config;

/// <summary>Round-trips against the real Windows Credential Manager (the C# CI job runs on
/// windows-latest). Each test uses a unique, namespaced handle and deletes it in a finally,
/// so it leaves nothing behind.</summary>
public sealed class WindowsCredentialStoreTests
{
    [Fact]
    public void Set_then_Get_round_trips_the_secret_via_its_handle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registry = new SecretRegistry();
        var store = new WindowsCredentialStore(registry);
        string handle = Handle();
        try
        {
            Assert.Null(store.Read(handle)); // absent before write

            store.Write(handle, "sk-round-trip-123");

            Assert.Equal("sk-round-trip-123", store.Read(handle));
        }
        finally
        {
            store.Delete(handle);
        }

        Assert.Null(store.Read(handle)); // gone after delete
    }

    [Fact]
    public void Overwrites_an_existing_secret()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore(new SecretRegistry());
        string handle = Handle();
        try
        {
            store.Write(handle, "first");
            store.Write(handle, "second");

            Assert.Equal("second", store.Read(handle));
        }
        finally
        {
            store.Delete(handle);
        }
    }

    [Fact]
    public void Reading_registers_the_secret_for_log_redaction()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var registry = new SecretRegistry();
        var store = new WindowsCredentialStore(registry);
        string handle = Handle();
        try
        {
            store.Write(handle, "sk-should-be-redacted");
            store.Read(handle);

            Assert.Equal(
                $"key is {SecretRegistry.Placeholder}",
                registry.Redact("key is sk-should-be-redacted"));
        }
        finally
        {
            store.Delete(handle);
        }
    }

    [Fact]
    public void Deleting_an_absent_handle_is_a_no_op()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new WindowsCredentialStore(new SecretRegistry());

        store.Delete(Handle()); // does not throw
    }

    private static string Handle() => $"lore-test/{Guid.NewGuid():N}";
}
