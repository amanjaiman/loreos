using System.IO;

namespace Lore.Agent.Hosting;

/// <summary>Reads the tail of the agent's log file for <c>GET /system/log</c> (spec 005 T005).
/// The log path is a convention (<c>%LocalAppData%\Lore\lore.log</c>); the file is written
/// through the redacting sink, so anything served here is already scrubbed of secrets
/// (constitution §4.2). A missing file is not an error — it just means nothing has been logged
/// yet, so the tail is empty.</summary>
public sealed class LogTail
{
    private const int DefaultLines = 200;
    private const int MaxLines = 2000;

    private readonly string _path;

    /// <param name="path">Absolute path to the log file.</param>
    public LogTail(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>The default log location, alongside the memory data dir.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lore", "lore.log");

    /// <summary>The last <paramref name="maxLines"/> lines in file order (oldest first), or an
    /// empty list when the file does not exist yet.</summary>
    public async Task<IReadOnlyList<string>> ReadAsync(int? maxLines = null, CancellationToken cancellationToken = default)
    {
        int take = maxLines is null or <= 0 ? DefaultLines : Math.Min(maxLines.Value, MaxLines);
        if (!File.Exists(_path))
        {
            return [];
        }

        // Log files are small (a personal agent), so reading the whole file and slicing is
        // simpler and plenty fast. Opened shared so a concurrent writer isn't blocked.
        string[] lines = await ReadAllLinesSharedAsync(cancellationToken).ConfigureAwait(false);
        return lines.Length <= take ? lines : lines[^take..];
    }

    private async Task<string[]> ReadAllLinesSharedAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        using var stream = new FileStream(
            _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return [.. lines];
    }
}
