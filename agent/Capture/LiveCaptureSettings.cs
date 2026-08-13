using System.Text.Json.Nodes;

namespace Lore.Agent.Capture;

/// <summary>
/// The small, live-changing part of capture configuration. Timing and lifecycle thresholds are
/// fixed for a process lifetime; pause and privacy choices are replaced atomically after
/// <c>PATCH /config</c> so the running capture loop never uses a stale blocklist.
/// </summary>
public sealed class LiveCaptureSettings
{
    private Snapshot _current;

    public LiveCaptureSettings(CaptureOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _current = new Snapshot(
            options.Enabled,
            new Blocklist(options.BlocklistApps, options.BlocklistKeywords));
    }

    public bool Enabled => Volatile.Read(ref _current).Enabled;

    public Blocklist Blocklist => Volatile.Read(ref _current).Blocklist;

    /// <summary>Replace the live values from the persisted capture object.</summary>
    public void Update(JsonObject capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        Snapshot previous = Volatile.Read(ref _current);
        bool enabled = ReadBool(capture, "enabled") ?? previous.Enabled;
        IEnumerable<string> apps = ReadStrings(capture, "blocklistApps", "blocklist_apps")
            ?? previous.Blocklist.Apps;
        IReadOnlyList<string> keywords = ReadStrings(capture, "blocklistKeywords", "blocklist_keywords")
            ?? previous.Blocklist.Keywords;
        Volatile.Write(ref _current, new Snapshot(enabled, new Blocklist(apps, keywords)));
    }

    private static bool? ReadBool(JsonObject value, string name) =>
        value[name] is JsonValue node && node.TryGetValue(out bool result) ? result : null;

    private static string[]? ReadStrings(JsonObject value, params string[] names)
    {
        JsonArray? array = names
            .Select(name => value[name])
            .OfType<JsonArray>()
            .FirstOrDefault();
        return array?.Select(node => node?.GetValue<string>())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }

    private sealed record Snapshot(bool Enabled, Blocklist Blocklist);
}
