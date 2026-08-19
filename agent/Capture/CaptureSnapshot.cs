using System.Globalization;
using System.Text.Json.Nodes;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture;

/// <summary>Every capture value the running agent reads, as one immutable object (v2-008 R2).
/// <see cref="LiveCaptureSettings"/> swaps the whole record on <c>PATCH /config</c>, so a
/// consumer that reads it once has a self-consistent set of values for that unit of work and
/// picks the new ones up on its next read — no locks, no per-property atomics, no restart.
///
/// <para>This reverses a v2-007 decision that timing and lifecycle thresholds were
/// "process-lifetime configuration". They are about to become user-facing controls, and a
/// preferences control that needs a restart — and drops the open episode doing it — is not
/// acceptable.</para>
///
/// <para><b>This is also where capture config is validated</b> (see <see cref="From"/>), which is
/// why the record has a factory rather than being constructed directly by consumers.</para></summary>
/// <param name="Enabled">Master switch; false idles the loop.</param>
/// <param name="Attentiveness">The resolved "how closely Lore watches" stop (v2-008 R1.1).</param>
/// <param name="Certainty">The resolved "how sure Lore has to be" stop (v2-008 R1.2).</param>
/// <param name="Detail">The resolved "how much detail" stop (v2-008 R1.3). The distiller reads
/// this for its detail directive alongside <paramref name="StatementMaxChars"/>.</param>
/// <param name="Blocklist">The user's apps and keywords, already normalized.</param>
/// <param name="PollInterval">Gap between foreground-window polls.</param>
/// <param name="DwellThreshold">Focus a window must hold to become a capture candidate.</param>
/// <param name="RecaptureInterval">Minimum gap before re-reading a wholly unchanged window.</param>
/// <param name="TitleRecaptureInterval">Minimum gap before re-reading the same window handle
/// after a title-only change.</param>
/// <param name="StatementMaxChars">Character cap the distiller is told to keep a statement
/// under.</param>
/// <param name="Episodes">Episode segmentation thresholds.</param>
/// <param name="Lifecycle">Lifecycle routing thresholds and the daily promotion budget.</param>
/// <param name="Diagnostics">Opt-in raw-capture troubleshooting switch.</param>
/// <param name="RetentionDays">Days of evidence kept; 0 or less means forever.</param>
public sealed record CaptureSnapshot(
    bool Enabled,
    AttentivenessPreset Attentiveness,
    CertaintyPreset Certainty,
    DetailPreset Detail,
    Blocklist Blocklist,
    TimeSpan PollInterval,
    TimeSpan DwellThreshold,
    TimeSpan RecaptureInterval,
    TimeSpan TitleRecaptureInterval,
    int StatementMaxChars,
    EpisodeOptions Episodes,
    LifecycleOptions Lifecycle,
    bool Diagnostics,
    int RetentionDays)
{
    /// <summary>The property <c>GET /config</c> hangs the read-only effective values off
    /// (v2-008 R3). Never persisted — see <see cref="ToResolvedJson"/>.</summary>
    public const string ResolvedProperty = "resolved";

    /// <summary>A poll interval above this is treated as a typo rather than a preference. The
    /// tick is not only how often a window is read — it is also what drives
    /// <c>EpisodeBuilder.CloseIfIdle</c>, so a value anywhere near
    /// <see cref="EpisodeOptions.IdleTimeout"/> (10 minutes) stops episodes closing on time. The
    /// most attentiveness ever asks for is 5 seconds; a minute is twelve times that.
    ///
    /// <para>The concrete accident this catches: <c>"pollInterval": 2</c> in a hand-edited
    /// config.json. The configuration binder parses a bare <c>2</c> as a TimeSpan of <b>2 days</b>,
    /// not 2 seconds.</para></summary>
    internal static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(1);

    /// <summary>Build a snapshot from bound options, <b>repairing anything out of range</b>.
    ///
    /// <para>This is the single place capture config crosses into the running agent — both the
    /// startup binding and every <c>PATCH /config</c> come through here — so it is the one place
    /// validation belongs. Before v2-008 the checks lived in <c>EpisodeBuilder</c>'s constructor,
    /// where they could only ever run once and could only fail one way: by throwing, at DI
    /// resolution, taking the whole agent down over a mistyped number. With the values live that
    /// is no longer an option at all — a bad value would arrive on a background thread mid-episode
    /// — and it was never the right answer for a local-first app whose config file the user is
    /// invited to edit. <b>Garbage never crashes the capture loop; it is replaced with the
    /// default and logged.</b></para>
    ///
    /// <para>What is checked is exactly what can break the loop: values that make
    /// <c>Task.Delay</c> throw, make a substring index negative, or close an episode on every
    /// single observation. The lifecycle thresholds are not checked because none of them has such
    /// a path — an out-of-band confidence or similarity number only ever routes a candidate
    /// somewhere unhelpful, which is a tuning mistake the user can see and undo, not a
    /// fault.</para>
    ///
    /// <para><b>The repair value is the resolved preset, not the built-in default</b> (v2-008 R3).
    /// A user on <c>light</c> who mistypes <c>episodes.maxObservations</c> gets light's 30 back,
    /// not balanced's 48 — the same value they would have had by deleting the key. Repairing to
    /// the shipped default would silently move them to a different stop for that one field.</para></summary>
    /// <param name="options">Bound (or merged) capture options; may hold anything. The three
    /// preset names on it decide both the resolved stops and the repair values.</param>
    /// <param name="logger">Optional; receives one warning per repaired field and per
    /// unrecognised preset name.</param>
    public static CaptureSnapshot From(CaptureOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Resolving here as well as at the merge is deliberate: it is what makes the repair values
        // preset-aware, and it is the one place the warning for a garbage preset name is logged
        // (the merge resolves silently so a single PATCH cannot log the same typo twice).
        CaptureOptions preset = CapturePresets.Resolve(options, logger);
        return new CaptureSnapshot(
            options.Enabled,
            Stop(preset.Attentiveness, AttentivenessPreset.Balanced),
            Stop(preset.Certainty, CertaintyPreset.Balanced),
            Stop(preset.Detail, DetailPreset.Balanced),
            new Blocklist(options.BlocklistApps ?? [], options.BlocklistKeywords ?? []),
            Checked(
                options.PollInterval,
                options.PollInterval > TimeSpan.Zero && options.PollInterval <= MaxPollInterval,
                preset.PollInterval, "capture.pollInterval", logger),
            Checked(
                options.DwellThreshold, options.DwellThreshold >= TimeSpan.Zero,
                preset.DwellThreshold, "capture.dwellThreshold", logger),
            Checked(
                options.RecaptureInterval, options.RecaptureInterval >= TimeSpan.Zero,
                preset.RecaptureInterval, "capture.recaptureInterval", logger),
            Checked(
                options.TitleRecaptureInterval, options.TitleRecaptureInterval >= TimeSpan.Zero,
                preset.TitleRecaptureInterval, "capture.titleRecaptureInterval", logger),
            Checked(
                options.StatementMaxChars, options.StatementMaxChars >= 1,
                preset.StatementMaxChars, "capture.statementMaxChars", logger),
            Repair(options.Episodes ?? preset.Episodes, preset.Episodes, logger),
            options.Lifecycle ?? preset.Lifecycle,
            options.Diagnostics,
            options.RetentionDays);
    }

    /// <summary>The effective values as a JSON object that <b>echoes the writable keys exactly</b>
    /// — same names, same formats (v2-008 R3). <c>GET /config</c> hangs this off
    /// <c>capture.resolved</c> so the Settings consequence lines can be derived from what is
    /// actually in force rather than from a preset name, and so a power user can see what a preset
    /// means.
    ///
    /// <para>Echoing the writable shape is the whole point: the obvious workflow for a tinkerer is
    /// to read <c>resolved</c>, copy a line into <c>capture</c> to pin it, and delete it later to
    /// go back. That only works if <c>"pollInterval": "00:00:02"</c> comes back as a TimeSpan
    /// string — as a plain <c>2</c> the binder would read it as two <b>days</b>.</para>
    ///
    /// <para>This is derived state and is <b>never persisted</b>:
    /// <see cref="Config.LoreConfig"/> strips it on every write, so it cannot round-trip into
    /// config.json through a read-modify-write. The blocklists are the one thing deliberately left
    /// out — no preset touches them, they are already echoed verbatim beside this block, and they
    /// can be long.</para></summary>
    public JsonObject ToResolvedJson() => new()
    {
        ["enabled"] = Enabled,
        ["attentiveness"] = CapturePresets.Name(Attentiveness),
        ["certainty"] = CapturePresets.Name(Certainty),
        ["detail"] = CapturePresets.Name(Detail),
        ["pollInterval"] = Text(PollInterval),
        ["dwellThreshold"] = Text(DwellThreshold),
        ["recaptureInterval"] = Text(RecaptureInterval),
        ["titleRecaptureInterval"] = Text(TitleRecaptureInterval),
        ["statementMaxChars"] = StatementMaxChars,
        ["retentionDays"] = RetentionDays,
        ["diagnostics"] = Diagnostics,
        ["episodes"] = new JsonObject
        {
            ["continuityGap"] = Text(Episodes.ContinuityGap),
            ["idleTimeout"] = Text(Episodes.IdleTimeout),
            ["maxAge"] = Text(Episodes.MaxAge),
            ["maxObservations"] = Episodes.MaxObservations,
            ["titleSimilarityThreshold"] = Episodes.TitleSimilarityThreshold,
            ["textSimilarityThreshold"] = Episodes.TextSimilarityThreshold,
            ["duplicateThreshold"] = Episodes.DuplicateThreshold,
            ["maxSamples"] = Episodes.MaxSamples,
            ["sampleMaxChars"] = Episodes.SampleMaxChars,
        },
        ["lifecycle"] = new JsonObject
        {
            ["sameFactThreshold"] = Lifecycle.SameFactThreshold,
            ["sameTopicThreshold"] = Lifecycle.SameTopicThreshold,
            ["highSignalConfidence"] = Lifecycle.HighSignalConfidence,
            ["dailyBudget"] = Lifecycle.DailyBudget,
            ["stagedTtlDays"] = Lifecycle.StagedTtlDays,
            ["defaultHorizonDays"] = Lifecycle.DefaultHorizonDays,
            ["minHorizonDays"] = Lifecycle.MinHorizonDays,
            ["maxHorizonDays"] = Lifecycle.MaxHorizonDays,
            ["reinforceBump"] = Lifecycle.ReinforceBump,
            ["confidenceCap"] = Lifecycle.ConfidenceCap,
            ["matchNeighbors"] = Lifecycle.MatchNeighbors,
        },
    };

    // "hh:mm:ss" — the one format the configuration binder's TimeSpan converter round-trips, so a
    // line copied out of `resolved` and into `capture` means what it read.
    private static string Text(TimeSpan value) => value.ToString("c", CultureInfo.InvariantCulture);

    // CapturePresets already repaired the name, so this only ever hits its fallback if a caller
    // hand-built a CaptureOptions with a name it never produced.
    private static TPreset Stop<TPreset>(string? name, TPreset fallback)
        where TPreset : struct, Enum =>
        Enum.TryParse(name, ignoreCase: true, out TPreset parsed) ? parsed : fallback;

    // The four bounds EpisodeBuilder's constructor used to enforce by throwing. Each one guards a
    // real failure, not a taste: below 2 observations or samples an episode cannot hold a
    // beginning and an end; a non-positive MaxAge closes every episode at its first observation;
    // a SampleMaxChars below 1 makes the truncating range throw on every close.
    private static EpisodeOptions Repair(
        EpisodeOptions options, EpisodeOptions preset, ILogger? logger) =>
        options with
        {
            MaxObservations = Checked(
                options.MaxObservations, options.MaxObservations >= 2,
                preset.MaxObservations, "capture.episodes.maxObservations", logger),
            MaxAge = Checked(
                options.MaxAge, options.MaxAge > TimeSpan.Zero,
                preset.MaxAge, "capture.episodes.maxAge", logger),
            MaxSamples = Checked(
                options.MaxSamples, options.MaxSamples >= 2,
                preset.MaxSamples, "capture.episodes.maxSamples", logger),
            SampleMaxChars = Checked(
                options.SampleMaxChars, options.SampleMaxChars >= 1,
                preset.SampleMaxChars, "capture.episodes.sampleMaxChars", logger),
        };

    private static T Checked<T>(T value, bool valid, T fallback, string key, ILogger? logger)
    {
        if (valid)
        {
            return value;
        }

        logger?.LogWarning(
            "config {Key} is out of range ({Value}); using the built-in default {Fallback} instead",
            key, value, fallback);
        return fallback;
    }
}
