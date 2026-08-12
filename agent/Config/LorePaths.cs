using System.IO;

namespace Lore.Agent.Config;

/// <summary>Where Lore keeps the user's data, and the one-time move that got it there.</summary>
/// <remarks>
/// The rule: the data root belongs to Lore alone, and sits inside no directory another tool
/// manages. It took two goes to get there.
///
/// <c>%LOCALAPPDATA%\Lore</c> is where the Squirrel installer puts the application, so the
/// install root and the data root were the same folder — installing over an existing copy
/// destroyed every memory, and uninstalling would have too. <c>%APPDATA%\Lore</c> fixed the
/// installer collision but landed on Electron's own <c>userData</c> directory, putting memories
/// beside the Chromium cache. Less dangerous, same mistake.
///
/// <c>%LOCALAPPDATA%\LoreData</c> is owned by nothing else. It also stays off roaming profiles,
/// which matters once the vector store is large.
/// </remarks>
public static class LorePaths
{
    /// <summary>Environment variable that redirects the data root to a throwaway location.</summary>
    /// <remarks>
    /// This exists for the installer's build gate (installer/verify-payload.ps1), which starts the
    /// freshly built agent to prove it can actually serve the local API. Without a redirect that
    /// smoke launch runs the real agent against the build machine's real state: it would migrate
    /// the developer's legacy data, read their config.json (and the API key in it), and start the
    /// capture loop over their screen. A build must not do any of that.
    ///
    /// It is not a user-facing setting. Nothing ships that sets it, and an unset or blank value
    /// leaves the normal root below untouched.
    /// </remarks>
    public const string DataDirectoryVariable = "LORE_DATA_DIR";

    /// <summary>Whether <see cref="DataDirectoryVariable"/> supplied the root. When it did, the
    /// legacy migration is skipped — pointing the agent at a scratch directory must never become
    /// a way to move the real data set into it.</summary>
    public static bool IsDataDirectoryOverridden { get; } =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    /// <summary>The data root: config, logs, the activity store, and the memory store.</summary>
    public static string DataDirectory { get; } = ResolveDataDirectory();

    private static string ResolveDataDirectory()
    {
        string? overridden = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LoreData")
            : Path.GetFullPath(overridden);
    }

    /// <summary>Every location data has previously lived, newest first — the order matters,
    /// since the migration never overwrites and so the first one to supply a file wins.</summary>
    public static IReadOnlyList<string> LegacyDataDirectories { get; } =
    [
        // Electron's userData: where the installer-collision fix briefly put things.
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lore"),
        // The original, which the Squirrel installer owns and wipes.
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore"),
    ];

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string LogFile => Path.Combine(DataDirectory, "lore.log");

    /// <summary>Data artifacts, named explicitly. The legacy directories are shared with other
    /// tools — the installer's files (<c>app-x.y.z</c>, <c>packages</c>, <c>Update.exe</c>) in one,
    /// Electron's cache (<c>GPUCache</c>, <c>Local Storage</c>, <c>Preferences</c>) in the other.
    /// Moving a folder wholesale would drag those along and break what owns them.</summary>
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
    public static void MigrateLegacyData()
    {
        // A redirected root is a scratch directory, and the legacy locations are the real ones.
        // Migrating here would move the developer's actual data set into it — the precise harm
        // the redirect exists to avoid — so an overridden root migrates nothing.
        if (IsDataDirectoryOverridden)
        {
            return;
        }

        foreach (string legacy in LegacyDataDirectories)
        {
            MigrateLegacyData(legacy, DataDirectory);
        }
    }

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
