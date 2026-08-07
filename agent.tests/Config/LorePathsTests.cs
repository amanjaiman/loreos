using System.IO;
using Lore.Agent.Config;

namespace Lore.Agent.Tests.Config;

/// <summary>
/// The data root used to be the installer's root, so installing over an existing copy destroyed
/// every memory. These cover the move off it — and, just as importantly, that the move does not
/// drag the installation along with it.
/// </summary>
public sealed class LorePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "lore-paths-" + Guid.NewGuid().ToString("N"));

    private string Legacy => Path.Combine(_root, "legacy");

    private string Data => Path.Combine(_root, "data");

    private void SeedLegacy()
    {
        Directory.CreateDirectory(Legacy);
        File.WriteAllText(Path.Combine(Legacy, "config.json"), "{\"provider\":{}}");
        File.WriteAllText(Path.Combine(Legacy, "activity.db"), "activity");
        File.WriteAllText(Path.Combine(Legacy, "history.db"), "history");
        File.WriteAllText(Path.Combine(Legacy, "lore.log"), "log");
        Directory.CreateDirectory(Path.Combine(Legacy, "qdrant", "collection"));
        File.WriteAllText(Path.Combine(Legacy, "qdrant", "meta.json"), "{}");
    }

    /// <summary>What Squirrel puts in the same folder. None of it is ours to move.</summary>
    private void SeedInstallation()
    {
        Directory.CreateDirectory(Path.Combine(Legacy, "app-0.1.0"));
        Directory.CreateDirectory(Path.Combine(Legacy, "packages"));
        File.WriteAllText(Path.Combine(Legacy, "Update.exe"), "squirrel");
        File.WriteAllText(Path.Combine(Legacy, "Lore.exe"), "stub");
    }

    [Fact]
    public void Migration_moves_the_data_set()
    {
        SeedLegacy();

        LorePaths.MigrateLegacyData(Legacy, Data);

        Assert.Equal("{\"provider\":{}}", File.ReadAllText(Path.Combine(Data, "config.json")));
        Assert.Equal("activity", File.ReadAllText(Path.Combine(Data, "activity.db")));
        Assert.Equal("history", File.ReadAllText(Path.Combine(Data, "history.db")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(Data, "lore.log")));
        Assert.True(File.Exists(Path.Combine(Data, "qdrant", "meta.json")));
        Assert.False(File.Exists(Path.Combine(Legacy, "config.json")));
    }

    [Fact]
    public void Migration_leaves_the_installation_alone()
    {
        SeedLegacy();
        SeedInstallation();

        LorePaths.MigrateLegacyData(Legacy, Data);

        // The installer still owns a working directory.
        Assert.True(Directory.Exists(Path.Combine(Legacy, "app-0.1.0")));
        Assert.True(Directory.Exists(Path.Combine(Legacy, "packages")));
        Assert.True(File.Exists(Path.Combine(Legacy, "Update.exe")));
        Assert.True(File.Exists(Path.Combine(Legacy, "Lore.exe")));
        // ...and none of it followed the data across.
        Assert.False(Directory.Exists(Path.Combine(Data, "app-0.1.0")));
        Assert.False(File.Exists(Path.Combine(Data, "Update.exe")));
    }

    [Fact]
    public void Migration_never_overwrites_newer_data()
    {
        SeedLegacy();
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "config.json"), "current");

        LorePaths.MigrateLegacyData(Legacy, Data);

        Assert.Equal("current", File.ReadAllText(Path.Combine(Data, "config.json")));
        // The stale copy stays put rather than being silently destroyed.
        Assert.True(File.Exists(Path.Combine(Legacy, "config.json")));
    }

    [Fact]
    public void Migration_is_idempotent_and_safe_with_nothing_to_move()
    {
        LorePaths.MigrateLegacyData(Legacy, Data); // legacy does not exist at all
        SeedLegacy();
        LorePaths.MigrateLegacyData(Legacy, Data);
        LorePaths.MigrateLegacyData(Legacy, Data);

        Assert.True(File.Exists(Path.Combine(Data, "activity.db")));
    }

    [Fact]
    public void Data_root_is_not_inside_the_installer_root()
    {
        Assert.False(
            LorePaths.DataDirectory.StartsWith(
                LorePaths.LegacyDataDirectory, StringComparison.OrdinalIgnoreCase),
            "user data must never live inside a directory an installer owns");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
