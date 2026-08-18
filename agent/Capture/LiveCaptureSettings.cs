using System.Globalization;
using System.Text.Json.Nodes;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture;

/// <summary>The one live source of capture configuration. Everything the running agent reads —
/// pause, blocklist, timings, episode and lifecycle thresholds, retention and diagnostics — is
/// served from a <see cref="CaptureSnapshot"/> that is replaced <b>atomically</b> after
/// <c>PATCH /config</c>, so no consumer ever needs a restart to see a settings change and none
/// can observe a half-applied one.
///
/// <para>Before v2-008 this carried only pause + blocklist and the rest was frozen for the
/// process lifetime. R2 reverses that: those values are becoming user-facing controls, and a
/// preferences control that needs a restart — dropping the open episode on the way — is not
/// acceptable. Consumers hold this object, not the values, and read
/// <see cref="Current"/> per unit of work.</para>
///
/// <para>Concurrency: one <c>Volatile.Read</c>/<c>Volatile.Write</c> of a single immutable
/// reference. There are no locks and no per-property atomics on purpose — a reader either sees
/// the whole old snapshot or the whole new one.</para></summary>
public sealed class LiveCaptureSettings
{
    private readonly ILogger<LiveCaptureSettings>? _logger;

    private CaptureSnapshot _current;

    public LiveCaptureSettings(CaptureOptions options, ILogger<LiveCaptureSettings>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _current = CaptureSnapshot.From(options, logger);
    }

    /// <summary>The values in force right now. Read this <b>once</b> per unit of work — a poll
    /// tick, one <c>Add</c>, one sweep — and use that snapshot throughout, so the work is decided
    /// against one consistent set of numbers even if a PATCH lands mid-way.</summary>
    public CaptureSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Shorthand for <c>Current.Enabled</c>. The pause boundary is re-read at the
    /// narrowest possible moments (e.g. after extraction), so it keeps its own accessor.</summary>
    public bool Enabled => Current.Enabled;

    /// <summary>Shorthand for <c>Current.Blocklist</c>.</summary>
    public Blocklist Blocklist => Current.Blocklist;

    /// <summary>Replace the live values from the persisted capture object.
    ///
    /// <para><c>PATCH /config</c> deep-merges and hands over the <b>whole</b> capture section, not
    /// the patch, so a key the caller did not mention is still present here if config.json holds
    /// it. A key that is genuinely absent keeps the value already in force: the startup binding
    /// draws on configuration sources other than config.json, and a blocklist edit must not
    /// silently discard a timing set through one of them. (T004 replaces that fallback base with
    /// the resolved preset, which is what lets deleting a raw key restore the preset's value.)</para>
    ///
    /// <para>Values are read exactly as the startup binder reads them — case-insensitive names,
    /// TimeSpans as <c>"hh:mm:ss"</c> strings, numbers as numbers or numeric strings — so what
    /// boots is what applies live. Anything unparseable is treated as absent rather than as zero;
    /// anything out of range is repaired by <see cref="CaptureSnapshot.From"/>.</para></summary>
    public void Update(JsonObject capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        CaptureSnapshot previous = Volatile.Read(ref _current);
        var merged = new CaptureOptions
        {
            Enabled = ReadBool(capture, "enabled") ?? previous.Enabled,
            PollInterval = ReadTimeSpan(capture, "pollInterval") ?? previous.PollInterval,
            DwellThreshold = ReadTimeSpan(capture, "dwellThreshold") ?? previous.DwellThreshold,
            RecaptureInterval = ReadTimeSpan(capture, "recaptureInterval") ?? previous.RecaptureInterval,
            TitleRecaptureInterval =
                ReadTimeSpan(capture, "titleRecaptureInterval") ?? previous.TitleRecaptureInterval,
            Episodes = MergeEpisodes(Section(capture, "episodes"), previous.Episodes),
            Lifecycle = MergeLifecycle(Section(capture, "lifecycle"), previous.Lifecycle),
            RetentionDays = ReadInt(capture, "retentionDays") ?? previous.RetentionDays,
            Diagnostics = ReadBool(capture, "diagnostics") ?? previous.Diagnostics,
            BlocklistApps = ReadStrings(capture, "blocklistApps", "blocklist_apps")
                ?? [.. previous.Blocklist.Apps],
            BlocklistKeywords = ReadStrings(capture, "blocklistKeywords", "blocklist_keywords")
                ?? previous.Blocklist.Keywords,
        };
        Volatile.Write(ref _current, CaptureSnapshot.From(merged, _logger));
    }

    private static EpisodeOptions MergeEpisodes(JsonObject? section, EpisodeOptions previous) =>
        section is null
            ? previous
            : previous with
            {
                ContinuityGap = ReadTimeSpan(section, "continuityGap") ?? previous.ContinuityGap,
                IdleTimeout = ReadTimeSpan(section, "idleTimeout") ?? previous.IdleTimeout,
                MaxAge = ReadTimeSpan(section, "maxAge") ?? previous.MaxAge,
                MaxObservations = ReadInt(section, "maxObservations") ?? previous.MaxObservations,
                TitleSimilarityThreshold =
                    ReadDouble(section, "titleSimilarityThreshold") ?? previous.TitleSimilarityThreshold,
                TextSimilarityThreshold =
                    ReadDouble(section, "textSimilarityThreshold") ?? previous.TextSimilarityThreshold,
                DuplicateThreshold = ReadDouble(section, "duplicateThreshold") ?? previous.DuplicateThreshold,
                MaxSamples = ReadInt(section, "maxSamples") ?? previous.MaxSamples,
                SampleMaxChars = ReadInt(section, "sampleMaxChars") ?? previous.SampleMaxChars,
            };

    private static LifecycleOptions MergeLifecycle(JsonObject? section, LifecycleOptions previous) =>
        section is null
            ? previous
            : previous with
            {
                SameFactThreshold = ReadDouble(section, "sameFactThreshold") ?? previous.SameFactThreshold,
                SameTopicThreshold = ReadDouble(section, "sameTopicThreshold") ?? previous.SameTopicThreshold,
                HighSignalConfidence =
                    ReadDouble(section, "highSignalConfidence") ?? previous.HighSignalConfidence,
                DailyBudget = ReadInt(section, "dailyBudget") ?? previous.DailyBudget,
                StagedTtlDays = ReadInt(section, "stagedTtlDays") ?? previous.StagedTtlDays,
                DefaultHorizonDays = ReadInt(section, "defaultHorizonDays") ?? previous.DefaultHorizonDays,
                MinHorizonDays = ReadInt(section, "minHorizonDays") ?? previous.MinHorizonDays,
                MaxHorizonDays = ReadInt(section, "maxHorizonDays") ?? previous.MaxHorizonDays,
                ReinforceBump = ReadDouble(section, "reinforceBump") ?? previous.ReinforceBump,
                ConfidenceCap = ReadDouble(section, "confidenceCap") ?? previous.ConfidenceCap,
                MatchNeighbors = ReadInt(section, "matchNeighbors") ?? previous.MatchNeighbors,
            };

    // Property lookup matching the configuration binder's case-insensitivity, so a key that binds
    // at startup also applies live rather than being silently ignored by one path of the two.
    private static JsonNode? Find(JsonObject value, string name)
    {
        if (value.TryGetPropertyValue(name, out JsonNode? exact))
        {
            return exact;
        }

        foreach (KeyValuePair<string, JsonNode?> pair in value)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static JsonObject? Section(JsonObject value, string name) => Find(value, name) as JsonObject;

    private static bool? ReadBool(JsonObject value, string name) => Find(value, name) switch
    {
        JsonValue node when node.TryGetValue(out bool result) => result,
        JsonValue node when node.TryGetValue(out string? text) && bool.TryParse(text, out bool parsed) => parsed,
        _ => null,
    };

    private static int? ReadInt(JsonObject value, string name) => Find(value, name) switch
    {
        JsonValue node when node.TryGetValue(out int result) => result,
        JsonValue node when node.TryGetValue(out string? text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) => parsed,
        _ => null,
    };

    private static double? ReadDouble(JsonObject value, string name) => Find(value, name) switch
    {
        JsonValue node when node.TryGetValue(out double result) => result,
        JsonValue node when node.TryGetValue(out string? text)
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => null,
    };

    // TimeSpans are strings only, exactly as the binder's TypeConverter takes them. A bare number
    // is deliberately NOT read as seconds: the binder would parse `2` as two DAYS, and a live path
    // that disagreed with the boot path about what a value means is worse than one that ignores it
    // (CaptureSnapshot.From then catches the two-day poll interval either way).
    private static TimeSpan? ReadTimeSpan(JsonObject value, string name) =>
        Find(value, name) is JsonValue node
        && node.TryGetValue(out string? text)
        && TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out TimeSpan result)
            ? result
            : null;

    private static string[]? ReadStrings(JsonObject value, params string[] names)
    {
        JsonArray? array = names
            .Select(name => Find(value, name))
            .OfType<JsonArray>()
            .FirstOrDefault();
        return array?.Select(node =>
                node is JsonValue value && value.TryGetValue(out string? text) ? text : null)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .ToArray();
    }
}
