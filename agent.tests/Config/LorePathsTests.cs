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

    /// <summary>What Squirrel and Electron put in the folders data used to share with them.
    /// None of it is ours to move.</summary>
    private void SeedForeignFiles()
    {
        // Squirrel's installation.
        Directory.CreateDirectory(Path.Combine(Legacy, "app-0.1.0"));
        Directory.CreateDirectory(Path.Combine(Legacy, "packages"));
        File.WriteAllText(Path.Combine(Legacy, "Update.exe"), "squirrel");
        File.WriteAllText(Path.Combine(Legacy, "Lore.exe"), "stub");
        // Electron's userData.
        Directory.CreateDirectory(Path.Combine(Legacy, "GPUCache"));
        Directory.CreateDirectory(Path.Combine(Legacy, "Local Storage"));
        File.WriteAllText(Path.Combine(Legacy, "Preferences"), "{}");
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
    public void Migration_leaves_other_tools_files_alone()
    {
        SeedLegacy();
        SeedForeignFiles();

        LorePaths.MigrateLegacyData(Legacy, Data);

        // The installer and Electron still own working directories.
        Assert.True(Directory.Exists(Path.Combine(Legacy, "app-0.1.0")));
        Assert.True(Directory.Exists(Path.Combine(Legacy, "packages")));
        Assert.True(File.Exists(Path.Combine(Legacy, "Update.exe")));
        Assert.True(Directory.Exists(Path.Combine(Legacy, "GPUCache")));
        Assert.True(File.Exists(Path.Combine(Legacy, "Preferences")));
        // ...and none of it followed the data across.
        Assert.False(Directory.Exists(Path.Combine(Data, "app-0.1.0")));
        Assert.False(File.Exists(Path.Combine(Data, "Update.exe")));
        Assert.False(Directory.Exists(Path.Combine(Data, "GPUCache")));
        Assert.False(File.Exists(Path.Combine(Data, "Preferences")));
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
    public void Data_root_is_not_inside_any_directory_another_tool_owns()
    {
        foreach (string legacy in LorePaths.LegacyDataDirectories)
        {
            // Containment, not string prefix: "...\LoreData" starts with "...\Lore" as text
            // while being a sibling directory, and conflating the two is how a check like this
            // ends up either useless or wrong.
            string inside = legacy.TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            Assert.False(
                LorePaths.DataDirectory.StartsWith(inside, StringComparison.OrdinalIgnoreCase),
                $"user data must never live inside {legacy}, which another tool manages");
            Assert.NotEqual(legacy, LorePaths.DataDirectory, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Newer_data_wins_when_several_legacy_locations_exist()
    {
        // Both previous homes still hold a copy; the migration runs newest-first and never
        // overwrites, so the newer one must be the copy that survives.
        string newer = Path.Combine(_root, "newer");
        string older = Path.Combine(_root, "older");
        Directory.CreateDirectory(newer);
        Directory.CreateDirectory(older);
        File.WriteAllText(Path.Combine(newer, "config.json"), "newer");
        File.WriteAllText(Path.Combine(older, "config.json"), "older");

        LorePaths.MigrateLegacyData(newer, Data);
        LorePaths.MigrateLegacyData(older, Data);

        Assert.Equal("newer", File.ReadAllText(Path.Combine(Data, "config.json")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
