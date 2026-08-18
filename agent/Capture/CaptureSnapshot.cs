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
/// <param name="Blocklist">The user's apps and keywords, already normalized.</param>
/// <param name="PollInterval">Gap between foreground-window polls.</param>
/// <param name="DwellThreshold">Focus a window must hold to become a capture candidate.</param>
/// <param name="RecaptureInterval">Minimum gap before re-reading a wholly unchanged window.</param>
/// <param name="TitleRecaptureInterval">Minimum gap before re-reading the same window handle
/// after a title-only change.</param>
/// <param name="Episodes">Episode segmentation thresholds.</param>
/// <param name="Lifecycle">Lifecycle routing thresholds and the daily promotion budget.</param>
/// <param name="Diagnostics">Opt-in raw-capture troubleshooting switch.</param>
/// <param name="RetentionDays">Days of evidence kept; 0 or less means forever.</param>
public sealed record CaptureSnapshot(
    bool Enabled,
    Blocklist Blocklist,
    TimeSpan PollInterval,
    TimeSpan DwellThreshold,
    TimeSpan RecaptureInterval,
    TimeSpan TitleRecaptureInterval,
    EpisodeOptions Episodes,
    LifecycleOptions Lifecycle,
    bool Diagnostics,
    int RetentionDays)
{
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

    // The built-in defaults, used as the repair value for anything out of range.
    private static readonly CaptureOptions Defaults = new();

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
    /// fault.</para></summary>
    /// <param name="options">Bound (or merged) capture options; may hold anything.</param>
    /// <param name="logger">Optional; receives one warning per repaired field.</param>
    public static CaptureSnapshot From(CaptureOptions options, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new CaptureSnapshot(
            options.Enabled,
            new Blocklist(options.BlocklistApps ?? [], options.BlocklistKeywords ?? []),
            Checked(
                options.PollInterval,
                options.PollInterval > TimeSpan.Zero && options.PollInterval <= MaxPollInterval,
                Defaults.PollInterval, "capture.pollInterval", logger),
            Checked(
                options.DwellThreshold, options.DwellThreshold >= TimeSpan.Zero,
                Defaults.DwellThreshold, "capture.dwellThreshold", logger),
            Checked(
                options.RecaptureInterval, options.RecaptureInterval >= TimeSpan.Zero,
                Defaults.RecaptureInterval, "capture.recaptureInterval", logger),
            Checked(
                options.TitleRecaptureInterval, options.TitleRecaptureInterval >= TimeSpan.Zero,
                Defaults.TitleRecaptureInterval, "capture.titleRecaptureInterval", logger),
            Repair(options.Episodes ?? Defaults.Episodes, logger),
            options.Lifecycle ?? Defaults.Lifecycle,
            options.Diagnostics,
            options.RetentionDays);
    }

    // The four bounds EpisodeBuilder's constructor used to enforce by throwing. Each one guards a
    // real failure, not a taste: below 2 observations or samples an episode cannot hold a
    // beginning and an end; a non-positive MaxAge closes every episode at its first observation;
    // a SampleMaxChars below 1 makes the truncating range throw on every close.
    private static EpisodeOptions Repair(EpisodeOptions options, ILogger? logger) =>
        options with
        {
            MaxObservations = Checked(
                options.MaxObservations, options.MaxObservations >= 2,
                Defaults.Episodes.MaxObservations, "capture.episodes.maxObservations", logger),
            MaxAge = Checked(
                options.MaxAge, options.MaxAge > TimeSpan.Zero,
                Defaults.Episodes.MaxAge, "capture.episodes.maxAge", logger),
            MaxSamples = Checked(
                options.MaxSamples, options.MaxSamples >= 2,
                Defaults.Episodes.MaxSamples, "capture.episodes.maxSamples", logger),
            SampleMaxChars = Checked(
                options.SampleMaxChars, options.SampleMaxChars >= 1,
                Defaults.Episodes.SampleMaxChars, "capture.episodes.sampleMaxChars", logger),
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
