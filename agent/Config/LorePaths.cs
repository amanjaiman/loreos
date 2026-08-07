using System.IO;

namespace Lore.Agent.Config;

/// <summary>Where Lore keeps the user's data, and the one-time move that got it there.</summary>
/// <remarks>
/// This used to be <c>%LOCALAPPDATA%\Lore</c> — which is also where the Squirrel installer puts
/// the application. The install root and the data root were the same folder, so installing over
/// an existing copy destroyed every memory, and uninstalling would have done the same. For a
/// product whose entire value is the data it accumulates, that is the worst bug available.
///
/// Data now lives in Roaming AppData, which is both where Electron puts its own userData and a
/// place no installer writes to. Nothing here is ever inside a directory some other tool owns.
/// </remarks>
public static class LorePaths
{
    /// <summary>The data root: config, logs, the activity store, and the memory store.</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lore");

    /// <summary>The pre-migration location, which the installer also owns.</summary>
    public static string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore");

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string LogFile => Path.Combine(DataDirectory, "lore.log");

    /// <summary>Data artifacts, named explicitly. The legacy directory also contains the
    /// installer's own files (<c>app-x.y.z</c>, <c>packages</c>, <c>Update.exe</c>), and moving
    /// the folder wholesale would drag those along and break the installation.</summary>
    private static readonly string[] LegacyFiles =
        ["config.json", "activity.db", "history.db", "lore.log"];

    private static readonly string[] LegacyDirectories = ["qdrant"];

    /// <summary>Move an existing data set to the new root, once, on startup.</summary>
    /// <remarks>
    /// Only ever moves an item the destination does not already have, so it cannot overwrite
    /// newer data, and it is safe to run on every launch. A failure here is not worth refusing to
    /// start over — the agent carries on with whatever it could move, and the untouched originals
    /// stay where they are.
    /// </remarks>
    public static void MigrateLegacyData() =>
        MigrateLegacyData(LegacyDataDirectory, DataDirectory);

    /// <summary>The move itself, against explicit directories so it can be tested.</summary>
    public static void MigrateLegacyData(string legacyDirectory, string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(legacyDirectory);
        ArgumentNullException.ThrowIfNull(dataDirectory);
        if (!Directory.Exists(legacyDirectory)
            || string.Equals(legacyDirectory, dataDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(dataDirectory);

            foreach (string name in LegacyFiles)
            {
                string from = Path.Combine(legacyDirectory, name);
                string to = Path.Combine(dataDirectory, name);
                if (File.Exists(from) && !File.Exists(to))
                {
                    File.Move(from, to);
                }
            }

            foreach (string name in LegacyDirectories)
            {
                string from = Path.Combine(legacyDirectory, name);
                string to = Path.Combine(dataDirectory, name);
                if (Directory.Exists(from) && !Directory.Exists(to))
                {
                    Directory.Move(from, to);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing was lost: whatever failed to move is still in the legacy directory.
        }
    }
}
