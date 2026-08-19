using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture;

/// <summary>How closely Lore watches (v2-008 R1.1). The tradeoff is how much Lore notices
/// against battery, CPU, and how many AI calls the user pays for.</summary>
public enum AttentivenessPreset
{
    /// <summary>Look less often: 5s polls, a 10s dwell, a 40s re-read.</summary>
    Light = 0,

    /// <summary>The shipped default.</summary>
    Balanced = 1,

    /// <summary>Look harder: a 3s dwell and a 15s re-read.</summary>
    Close = 2,
}

/// <summary>How sure Lore has to be before a fact leaves staging (v2-008 R1.2). Fewer, surer
/// memories against broader coverage with more noise — reversible in both directions.</summary>
public enum CertaintyPreset
{
    /// <summary>Promote only near-certain candidates; expire staged ones sooner.</summary>
    Strict = 0,

    /// <summary>The shipped default.</summary>
    Balanced = 1,

    /// <summary>Promote more readily and hold staged candidates longer.</summary>
    Eager = 2,
}

/// <summary>How much detail a memory records (v2-008 R1.3). Richer records against less
/// specific data on disk — the low stop is a privacy choice, not just a cheaper one.</summary>
public enum DetailPreset
{
    /// <summary>Terse statements from short samples.</summary>
    Minimal = 0,

    /// <summary>The shipped default.</summary>
    Balanced = 1,

    /// <summary>Specifics-first statements from longer samples.</summary>
    Rich = 2,
}

/// <summary>The three preset tables (v2-008 R1.1–R1.3) and the one place a preset name becomes
/// numbers. Config stores the <b>name</b>; the agent resolves it at read time, so the meaning of
/// a stop can be retuned in a release without rewriting anybody's config.json.
///
/// <para><b>What this produces is a base, not an answer.</b> Both callers —
/// <see cref="CaptureServiceCollectionExtensions"/> at startup and
/// <see cref="LiveCaptureSettings.Update"/> on every <c>PATCH /config</c> — resolve the presets
/// first and then lay the raw keys that are <em>explicitly present</em> in config.json on top, so
/// an explicit value wins for its own field and its siblings still come from the preset. Delete
/// the raw key and the preset's value returns on the next PATCH, with no restart. That split is
/// the point of R3: two controls for everyone, every knob for anyone who opens the file.</para>
///
/// <para>An unknown or misspelled name resolves to <c>balanced</c> and logs a warning. Nothing
/// here throws: these values are read on the capture loop's thread, and a hand-edited config.json
/// must never be able to take that loop down.</para></summary>
public static class CapturePresets
{
    /// <summary>Resolve the three preset names carried on <paramref name="raw"/> into a full set of
    /// capture values. Everything no preset touches — <c>ContinuityGap</c>, <c>IdleTimeout</c>,
    /// <c>MaxAge</c>, the similarity thresholds, <c>DailyBudget</c>, retention, the blocklist —
    /// keeps its built-in default.</summary>
    /// <param name="raw">Options carrying the preset names; other values are ignored.</param>
    /// <param name="logger">Optional; receives one warning per unrecognised preset name.</param>
    public static CaptureOptions Resolve(CaptureOptions raw, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return Resolve(raw.Attentiveness, raw.Certainty, raw.Detail, logger);
    }

    /// <inheritdoc cref="Resolve(CaptureOptions, ILogger?)"/>
    /// <param name="attentiveness">Raw <c>capture.attentiveness</c>; null or unknown means balanced.</param>
    /// <param name="certainty">Raw <c>capture.certainty</c>; null or unknown means balanced.</param>
    /// <param name="detail">Raw <c>capture.detail</c>; null or unknown means balanced.</param>
    /// <param name="logger">Optional; receives one warning per unrecognised preset name.</param>
    public static CaptureOptions Resolve(
        string? attentiveness, string? certainty, string? detail, ILogger? logger = null) =>
        Resolve(
            Parse(attentiveness, "attentiveness", AttentivenessPreset.Balanced, logger),
            Parse(certainty, "certainty", CertaintyPreset.Balanced, logger),
            Parse(detail, "detail", DetailPreset.Balanced, logger));

    /// <summary>The preset tables themselves, applied over the built-in defaults.
    ///
    /// <para><b>The shipped defaults are the balanced column</b> — <c>Resolve</c> at all three
    /// balanced stops returns exactly <c>new CaptureOptions()</c>, pinned by a test. The tables
    /// still state balanced's numbers explicitly so that a later change to a default cannot
    /// silently move the middle stop.</para></summary>
    public static CaptureOptions Resolve(
        AttentivenessPreset attentiveness, CertaintyPreset certainty, DetailPreset detail)
    {
        var defaults = new CaptureOptions();
        return defaults with
        {
            Attentiveness = Name(attentiveness),
            Certainty = Name(certainty),
            Detail = Name(detail),

            // R1.1. PollInterval is the same 2s at balanced and close: below that the loop costs
            // more than the extra readings are worth, and dwell/re-read are the honest knobs.
            PollInterval = attentiveness switch
            {
                AttentivenessPreset.Light => TimeSpan.FromSeconds(5),
                AttentivenessPreset.Close => TimeSpan.FromSeconds(2),
                _ => TimeSpan.FromSeconds(2),
            },
            DwellThreshold = attentiveness switch
            {
                AttentivenessPreset.Light => TimeSpan.FromSeconds(10),
                AttentivenessPreset.Close => TimeSpan.FromSeconds(3),
                _ => TimeSpan.FromSeconds(4),
            },
            RecaptureInterval = attentiveness switch
            {
                AttentivenessPreset.Light => TimeSpan.FromSeconds(40),
                AttentivenessPreset.Close => TimeSpan.FromSeconds(15),
                _ => TimeSpan.FromSeconds(25),
            },

            // Not in R1.1's table — T003 added this knob after the table was written and left its
            // scaling to T004. It holds a retitling window at a constant ~2.5x the read rate of a
            // window sitting still (40/15, 25/10, 15/6), which is the ratio T003 sized 10s to get.
            // Scaling it with the other timings is what keeps that ratio constant: pinned at 10s a
            // `light` user would see retitling windows read 4x as often as everything else, which
            // is precisely the cost blow-out the knob exists to prevent.
            TitleRecaptureInterval = attentiveness switch
            {
                AttentivenessPreset.Light => TimeSpan.FromSeconds(15),
                AttentivenessPreset.Close => TimeSpan.FromSeconds(6),
                _ => TimeSpan.FromSeconds(10),
            },

            // R1.3. Both caps move together: the model cannot write "seat 14C" if the sample it
            // read was truncated before that text, so raising the output cap alone produces longer
            // statements with no more information in them. Consumed by the distiller (T005).
            StatementMaxChars = detail switch
            {
                DetailPreset.Minimal => 120,
                DetailPreset.Rich => 500,
                _ => 200,
            },

            Episodes = defaults.Episodes with
            {
                // R1.1 again, and the reason the control is honest: MaxObservations — not the
                // clock — is what ends most episodes, so it must scale with RecaptureInterval or
                // the control changes episode *length* instead of capture density. These pairings
                // hold every stop at a ~20-minute episode, so AI call volume stays roughly flat
                // across all three.
                MaxObservations = attentiveness switch
                {
                    AttentivenessPreset.Light => 30,
                    AttentivenessPreset.Close => 80,
                    _ => 48,
                },
                MaxSamples = attentiveness switch
                {
                    AttentivenessPreset.Light => 6,
                    AttentivenessPreset.Close => 12,
                    _ => 8,
                },
                SampleMaxChars = detail switch
                {
                    DetailPreset.Minimal => 400,
                    DetailPreset.Rich => 900,
                    _ => 600,
                },
            },

            Lifecycle = defaults.Lifecycle with
            {
                // R1.2. SameFactThreshold and SameTopicThreshold are deliberately NOT here: they
                // govern whether two statements are the same fact — a correctness property of
                // dedup and arbitration, not a matter of taste. Moving them to make Lore "more
                // eager" corrupts the store rather than filling it.
                HighSignalConfidence = certainty switch
                {
                    CertaintyPreset.Strict => 0.90,
                    CertaintyPreset.Eager => 0.70,
                    _ => 0.85,
                },
                StagedTtlDays = certainty switch
                {
                    CertaintyPreset.Strict => 7,
                    CertaintyPreset.Eager => 30,
                    _ => 14,
                },
            },
        };
    }

    /// <summary>The name a resolved preset is written as in <c>capture.resolved</c> — the same
    /// lowercase spelling the writable key takes, so a user can copy it back.</summary>
    public static string Name<TPreset>(TPreset preset)
        where TPreset : struct, Enum
    {
        // Every stop is a single PascalCase word, so lowering the first character is the camelCase
        // spelling config.json uses for everything else.
        string name = preset.ToString()!;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }

    // Matched against the declared names only, case-insensitively. Enum.TryParse would also accept
    // "1" and "Light, Close", neither of which is a thing a user meant to write.
    private static TPreset Parse<TPreset>(string? name, string key, TPreset fallback, ILogger? logger)
        where TPreset : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            // Absent is not a mistake — it is every config written before v2-008, and it means
            // balanced. Silent on purpose: this is the upgrade path, not a repair.
            return fallback;
        }

        foreach (TPreset candidate in Enum.GetValues<TPreset>())
        {
            if (string.Equals(candidate.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        logger?.LogWarning(
            "config capture.{Key} is not one of {Known} ({Value}); using {Fallback} instead",
            key, string.Join(", ", Enum.GetValues<TPreset>().Select(Name)), name, Name(fallback));
        return fallback;
    }
}
