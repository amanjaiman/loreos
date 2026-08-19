using System.Text.Json.Nodes;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Lore.Agent.Tests.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Capture;

/// <summary>v2-008 R1.1–R1.3 and R3: three preset names resolve to the spec's numbers, a raw key
/// explicitly present in config.json beats the preset for that field alone, deleting it puts the
/// preset's value back with no restart, and nothing a user can type stops the capture loop.</summary>
public sealed class CapturePresetsTests
{
    private static JsonObject Capture(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static CaptureSnapshot Live(string json)
    {
        var settings = new LiveCaptureSettings(new CaptureOptions());
        settings.Update(Capture(json));
        return settings.Current;
    }

    // ── R1.1: how closely Lore watches ─────────────────────────────────────────────

    [Theory]
    [InlineData("light", 5, 10, 40, 15, 30, 6)]
    [InlineData("balanced", 2, 4, 25, 10, 48, 8)]
    [InlineData("close", 2, 3, 15, 6, 80, 12)]
    public void Attentiveness_resolves_to_the_spec_table(
        string stop,
        int poll,
        int dwell,
        int recapture,
        int titleRecapture,
        int maxObservations,
        int maxSamples)
    {
        CaptureSnapshot snapshot = Live($$"""{ "attentiveness": "{{stop}}" }""");

        Assert.Equal(TimeSpan.FromSeconds(poll), snapshot.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(dwell), snapshot.DwellThreshold);
        Assert.Equal(TimeSpan.FromSeconds(recapture), snapshot.RecaptureInterval);
        Assert.Equal(TimeSpan.FromSeconds(titleRecapture), snapshot.TitleRecaptureInterval);
        Assert.Equal(maxObservations, snapshot.Episodes.MaxObservations);
        Assert.Equal(maxSamples, snapshot.Episodes.MaxSamples);
    }

    [Fact]
    public void Attentiveness_leaves_the_thresholds_the_spec_excludes_alone()
    {
        // R1.1: ContinuityGap, IdleTimeout and MaxAge are the same at every stop. They shape what
        // an episode IS; the control is about how densely it is sampled.
        var defaults = new EpisodeOptions();

        foreach (string stop in new[] { "light", "balanced", "close" })
        {
            EpisodeOptions episodes = Live($$"""{ "attentiveness": "{{stop}}" }""").Episodes;

            Assert.Equal(defaults.ContinuityGap, episodes.ContinuityGap);
            Assert.Equal(defaults.IdleTimeout, episodes.IdleTimeout);
            Assert.Equal(defaults.MaxAge, episodes.MaxAge);
        }
    }

    [Fact]
    public void Every_attentiveness_stop_holds_a_roughly_twenty_minute_episode()
    {
        // The pairing rule that makes the control honest (R1.1): MaxObservations, not the clock,
        // is what ends most episodes, so cap ÷ re-read must stay flat or the control would change
        // episode LENGTH — and with it AI spend — instead of capture density.
        foreach (string stop in new[] { "light", "balanced", "close" })
        {
            CaptureSnapshot snapshot = Live($$"""{ "attentiveness": "{{stop}}" }""");
            double minutes =
                snapshot.Episodes.MaxObservations * snapshot.RecaptureInterval.TotalMinutes;

            Assert.InRange(minutes, 19.0, 21.0);
        }
    }

    [Fact]
    public void The_title_only_gap_holds_its_ratio_to_the_full_re_read()
    {
        // T003 sized the title-only gap at 10s to put a retitling window at ~2.5x the read rate of
        // a window sitting still rather than ~12x. Scaling it with attentiveness is what keeps
        // that ratio; pinned at 10s, a `light` user would see retitling windows read 4x as often
        // as everything else — the cost blow-out the gap exists to prevent.
        foreach (string stop in new[] { "light", "balanced", "close" })
        {
            CaptureSnapshot snapshot = Live($$"""{ "attentiveness": "{{stop}}" }""");

            Assert.InRange(
                snapshot.RecaptureInterval / snapshot.TitleRecaptureInterval, 2.4, 2.7);
            Assert.True(snapshot.TitleRecaptureInterval > snapshot.PollInterval);
        }
    }

    // ── R1.2: how sure Lore has to be ──────────────────────────────────────────────

    [Theory]
    [InlineData("strict", 0.90, 7)]
    [InlineData("balanced", 0.85, 14)]
    [InlineData("eager", 0.70, 30)]
    public void Certainty_resolves_to_the_spec_table(
        string stop, double highSignalConfidence, int stagedTtlDays)
    {
        LifecycleOptions lifecycle = Live($$"""{ "certainty": "{{stop}}" }""").Lifecycle;

        Assert.Equal(highSignalConfidence, lifecycle.HighSignalConfidence);
        Assert.Equal(stagedTtlDays, lifecycle.StagedTtlDays);
    }

    [Fact]
    public void Certainty_never_touches_the_similarity_thresholds()
    {
        // R1.2's hard exclusion. SameFactThreshold and SameTopicThreshold decide whether two
        // statements are the SAME FACT — a correctness property of dedup and arbitration, not a
        // matter of taste. Moving them to make Lore "more eager" corrupts the store.
        var defaults = new LifecycleOptions();

        foreach (string stop in new[] { "strict", "balanced", "eager" })
        {
            LifecycleOptions lifecycle = Live($$"""{ "certainty": "{{stop}}" }""").Lifecycle;

            Assert.Equal(defaults.SameFactThreshold, lifecycle.SameFactThreshold);
            Assert.Equal(defaults.SameTopicThreshold, lifecycle.SameTopicThreshold);
            Assert.Equal(defaults.DailyBudget, lifecycle.DailyBudget);
        }
    }

    // ── R1.3: how much detail ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("minimal", 120, 400)]
    [InlineData("balanced", 200, 600)]
    [InlineData("rich", 500, 900)]
    public void Detail_moves_the_statement_cap_and_the_sample_cap_together(
        string stop, int statementMaxChars, int sampleMaxChars)
    {
        // Both or neither: the model cannot write "seat 14C" if the sample it read was truncated
        // before that text, so raising the output cap alone buys longer statements with no more
        // information in them. T005 consumes both from the snapshot.
        CaptureSnapshot snapshot = Live($$"""{ "detail": "{{stop}}" }""");

        Assert.Equal(statementMaxChars, snapshot.StatementMaxChars);
        Assert.Equal(sampleMaxChars, snapshot.Episodes.SampleMaxChars);
    }

    [Fact]
    public void The_resolved_detail_stop_reaches_the_snapshot_for_the_distiller()
    {
        // T005 needs the stop itself, not just the caps, for the prompt's detail directive.
        Assert.Equal(DetailPreset.Minimal, Live("""{ "detail": "minimal" }""").Detail);
        Assert.Equal(DetailPreset.Rich, Live("""{ "detail": "RICH" }""").Detail);
    }

    // ── R3: absent means the preset, present means the user ────────────────────────

    [Fact]
    public void A_config_with_no_preset_keys_at_all_resolves_to_balanced()
    {
        // The upgrade path, and the constitution-level constraint: every config written before
        // v2-008 must land on balanced with no user action.
        CaptureSnapshot snapshot = Live("""{ "blocklistApps": ["1password"] }""");

        Assert.Equal(AttentivenessPreset.Balanced, snapshot.Attentiveness);
        Assert.Equal(CertaintyPreset.Balanced, snapshot.Certainty);
        Assert.Equal(DetailPreset.Balanced, snapshot.Detail);
        Assert.Equal(TimeSpan.FromSeconds(25), snapshot.RecaptureInterval);
        Assert.Equal(48, snapshot.Episodes.MaxObservations);
    }

    [Fact]
    public void The_shipped_defaults_are_the_balanced_column()
    {
        // Balanced is the default in the only sense that matters: an unconfigured agent and an
        // explicitly-balanced one are the same agent. (Balanced is NOT byte-identical to the
        // pre-v2-008 defaults — re-read moved 30s → 25s and the cap 40 → 48 — so the defaults
        // moved with it, and this is what stops the two drifting apart again.)
        CaptureOptions balanced = CapturePresets.Resolve(
            AttentivenessPreset.Balanced, CertaintyPreset.Balanced, DetailPreset.Balanced);
        var defaults = new CaptureOptions();

        Assert.Equal(defaults.PollInterval, balanced.PollInterval);
        Assert.Equal(defaults.DwellThreshold, balanced.DwellThreshold);
        Assert.Equal(defaults.RecaptureInterval, balanced.RecaptureInterval);
        Assert.Equal(defaults.TitleRecaptureInterval, balanced.TitleRecaptureInterval);
        Assert.Equal(defaults.StatementMaxChars, balanced.StatementMaxChars);
        Assert.Equal(defaults.Episodes, balanced.Episodes);
        Assert.Equal(defaults.Lifecycle, balanced.Lifecycle);
    }

    [Fact]
    public void An_explicit_raw_key_beats_the_preset_for_that_field_alone()
    {
        // R3's acceptance, and the whole point of the design: two controls for everyone, every
        // knob for anyone who opens the file.
        CaptureSnapshot snapshot = Live(
            """{ "attentiveness": "light", "episodes": { "maxObservations": 64 } }""");

        Assert.Equal(64, snapshot.Episodes.MaxObservations);   // the user's
        Assert.Equal(6, snapshot.Episodes.MaxSamples);         // light's, its sibling in the table
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(40), snapshot.RecaptureInterval);
        Assert.Equal(AttentivenessPreset.Light, snapshot.Attentiveness);
    }

    [Fact]
    public void Deleting_the_raw_key_restores_the_preset_value_with_no_restart()
    {
        // The other half of R3's acceptance, and the reason T004 had to replace T003's fallback
        // base: while an absent key kept the value already in force, a delete did nothing at all.
        var settings = new LiveCaptureSettings(new CaptureOptions());
        settings.Update(Capture(
            """{ "attentiveness": "light", "episodes": { "maxObservations": 64 } }"""));
        Assert.Equal(64, settings.Current.Episodes.MaxObservations);

        // Same object, one key gone — exactly what config.json looks like after the user deletes
        // the line and something PATCHes.
        settings.Update(Capture("""{ "attentiveness": "light" }"""));

        Assert.Equal(30, settings.Current.Episodes.MaxObservations);
    }

    [Fact]
    public void A_raw_override_survives_a_preset_change()
    {
        // Moving a control must not quietly discard a pin the user typed into the file: the
        // override is per-field and outlives the stop it was written beside.
        var settings = new LiveCaptureSettings(new CaptureOptions());
        settings.Update(Capture(
            """{ "attentiveness": "light", "pollInterval": "00:00:07" }"""));

        settings.Update(Capture(
            """{ "attentiveness": "close", "pollInterval": "00:00:07" }"""));

        Assert.Equal(TimeSpan.FromSeconds(7), settings.Current.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), settings.Current.RecaptureInterval); // close's
    }

    [Fact]
    public void A_preset_change_can_never_empty_the_blocklist()
    {
        // The one field deliberately NOT on the "absent means the preset" rule. No preset touches
        // the blocklist, so there is nothing to revert to — and the wrong way to fail here is to
        // silently stop filtering and capture the thing the user most wanted left alone.
        var settings = new LiveCaptureSettings(new CaptureOptions
        {
            BlocklistApps = ["1password"],
            BlocklistKeywords = ["salary"],
        });

        settings.Update(Capture("""{ "attentiveness": "close" }"""));

        Assert.True(settings.Blocklist.MatchesApp("1password.exe"));
        Assert.True(settings.Blocklist.MatchesKeyword("salary review"));

        // An empty array is present, not absent, so clearing it deliberately still works.
        settings.Update(Capture("""{ "blocklistApps": [], "blocklistKeywords": [] }"""));

        Assert.False(settings.Blocklist.MatchesApp("1password.exe"));
        Assert.Empty(settings.Blocklist.Keywords);
    }

    [Fact]
    public void Presets_are_independent_of_each_other()
    {
        CaptureSnapshot snapshot = Live(
            """{ "attentiveness": "close", "certainty": "strict", "detail": "minimal" }""");

        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.RecaptureInterval);
        Assert.Equal(0.90, snapshot.Lifecycle.HighSignalConfidence);
        Assert.Equal(120, snapshot.StatementMaxChars);
        Assert.Equal(400, snapshot.Episodes.SampleMaxChars);
        Assert.Equal(12, snapshot.Episodes.MaxSamples); // close's, not minimal's business
    }

    // ── Nothing a user can type takes the loop down ────────────────────────────────

    [Fact]
    public void A_garbage_preset_name_falls_back_to_balanced_with_a_warning()
    {
        var logger = new CapturingLogger();

        CaptureSnapshot snapshot = CaptureSnapshot.From(
            new CaptureOptions { Attentiveness = "hawk-eyed", Certainty = "", Detail = "1" },
            logger);

        Assert.Equal(AttentivenessPreset.Balanced, snapshot.Attentiveness);
        Assert.Equal(TimeSpan.FromSeconds(25), snapshot.RecaptureInterval);
        Assert.Equal(48, snapshot.Episodes.MaxObservations);

        // "1" is rejected on purpose: Enum.TryParse would take it as the ordinal for balanced, and
        // a user who typed a number did not mean an enum ordinal.
        Assert.Equal(DetailPreset.Balanced, snapshot.Detail);
        Assert.Equal(2, logger.Messages.Count); // the empty string is absent, not a mistake
        Assert.Contains(
            logger.Messages,
            message => message.Contains("attentiveness", StringComparison.Ordinal)
                && message.Contains("hawk-eyed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_garbage_preset_name_never_throws_on_the_live_path()
    {
        // These values are read on the capture loop's thread. A throw here would kill the loop
        // mid-episode, which is why every repair is a log rather than an exception.
        CaptureSnapshot snapshot = Live(
            """{ "attentiveness": 3, "certainty": ["strict"], "detail": null }""");

        Assert.Equal(AttentivenessPreset.Balanced, snapshot.Attentiveness);
        Assert.Equal(CertaintyPreset.Balanced, snapshot.Certainty);
        Assert.Equal(DetailPreset.Balanced, snapshot.Detail);
    }

    [Fact]
    public void An_out_of_range_raw_value_is_repaired_to_the_preset_not_the_default()
    {
        // A `light` user who mistypes a cap gets light's 30 back — the value they would have had
        // by deleting the key. Repairing to the shipped 48 would silently move that one field to
        // a different stop.
        CaptureSnapshot snapshot = Live(
            """
            {
              "attentiveness": "light",
              "pollInterval": "00:00:00",
              "episodes": { "maxObservations": 1 }
            }
            """);

        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.PollInterval);
        Assert.Equal(30, snapshot.Episodes.MaxObservations);
    }

    // ── The startup path resolves the same way the live one does ───────────────────

    [Fact]
    public void The_startup_binding_lays_explicit_keys_over_the_preset()
    {
        // Presence is decided by the configuration binder here and by the JSON object on the live
        // path; both must reach the same snapshot, or a value would change meaning at the first
        // PATCH after a restart.
        var services = new ServiceCollection();
        services.AddCapturePipeline(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["capture:attentiveness"] = "close",
                ["capture:detail"] = "rich",
                ["capture:episodes:maxObservations"] = "64",
            })
            .Build());

        CaptureSnapshot snapshot = services.BuildServiceProvider()
            .GetRequiredService<LiveCaptureSettings>().Current;

        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.RecaptureInterval); // close's
        Assert.Equal(TimeSpan.FromSeconds(6), snapshot.TitleRecaptureInterval);
        Assert.Equal(900, snapshot.Episodes.SampleMaxChars);                // rich's
        Assert.Equal(64, snapshot.Episodes.MaxObservations);                // the user's
        Assert.Equal(12, snapshot.Episodes.MaxSamples);                     // close's sibling
    }

    [Fact]
    public void An_unconfigured_startup_binding_is_balanced()
    {
        var services = new ServiceCollection();
        services.AddCapturePipeline(new ConfigurationBuilder().Build());

        CaptureSnapshot snapshot = services.BuildServiceProvider()
            .GetRequiredService<LiveCaptureSettings>().Current;

        Assert.Equal(AttentivenessPreset.Balanced, snapshot.Attentiveness);
        Assert.Equal(TimeSpan.FromSeconds(25), snapshot.RecaptureInterval);
        Assert.Equal(48, snapshot.Episodes.MaxObservations);
        Assert.Equal(200, snapshot.StatementMaxChars);
    }

    // ── capture.resolved echoes the writable keys ──────────────────────────────────

    [Fact]
    public void Resolved_echoes_the_writable_keys_in_the_writable_shape()
    {
        JsonObject resolved = Live("""{ "attentiveness": "light", "detail": "rich" }""")
            .ToResolvedJson();

        // TimeSpans as "hh:mm:ss" strings, not seconds as numbers: `"pollInterval": 5` copied back
        // into `capture` would be read by the binder as five DAYS.
        Assert.Equal("00:00:05", (string?)resolved["pollInterval"]);
        Assert.Equal("00:00:40", (string?)resolved["recaptureInterval"]);
        Assert.Equal("00:00:15", (string?)resolved["titleRecaptureInterval"]);
        Assert.Equal("00:45:00", (string?)resolved["episodes"]!["maxAge"]);

        // Nested exactly as the writable block nests, and naming the preset stops it resolved.
        Assert.Equal(30, (int?)resolved["episodes"]!["maxObservations"]);
        Assert.Equal(900, (int?)resolved["episodes"]!["sampleMaxChars"]);
        Assert.Equal(0.85, (double?)resolved["lifecycle"]!["highSignalConfidence"]);
        Assert.Equal("light", (string?)resolved["attentiveness"]);
        Assert.Equal("balanced", (string?)resolved["certainty"]);
        Assert.Equal("rich", (string?)resolved["detail"]);
        Assert.Equal(500, (int?)resolved["statementMaxChars"]);
    }

    [Fact]
    public void A_line_copied_out_of_resolved_and_into_capture_pins_that_value()
    {
        // The workflow R3 exists to make correct by construction: read `resolved`, copy a line
        // into `capture`, and the value you read is the value you get — no unit-mismatch trap.
        JsonObject resolved = Live("""{ "attentiveness": "close" }""").ToResolvedJson();
        var pinned = new JsonObject
        {
            ["attentiveness"] = "light", // moved the control afterwards…
            ["recaptureInterval"] = resolved["recaptureInterval"]!.DeepClone(),
            ["episodes"] = new JsonObject
            {
                ["maxObservations"] = resolved["episodes"]!["maxObservations"]!.DeepClone(),
            },
        };

        var settings = new LiveCaptureSettings(new CaptureOptions());
        settings.Update(pinned);

        // …and the two pinned lines still read exactly what `resolved` said they would.
        Assert.Equal(TimeSpan.FromSeconds(15), settings.Current.RecaptureInterval);
        Assert.Equal(80, settings.Current.Episodes.MaxObservations);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.Current.PollInterval); // light's, unpinned
    }

    [Fact]
    public void Resolved_round_trips_through_the_writable_block_unchanged()
    {
        // Every key in `resolved` is a key `capture` accepts, spelled the same way and parsed the
        // same way. Feeding the whole block back in must be a no-op — if one name or format were
        // wrong, that key would silently fall back to its preset value here.
        CaptureSnapshot original = Live(
            """{ "attentiveness": "close", "certainty": "strict", "detail": "minimal" }""");

        var settings = new LiveCaptureSettings(new CaptureOptions());
        settings.Update(original.ToResolvedJson());

        // Blocklist compared separately: it is a class, so the record's generated equality would
        // compare it by reference. It is also the one thing `resolved` deliberately leaves out —
        // no preset touches it and it is already echoed verbatim beside the block.
        CaptureSnapshot current = settings.Current;
        Assert.Equal(original with { Blocklist = current.Blocklist }, current);
        Assert.Empty(current.Blocklist.Apps);
        Assert.Empty(current.Blocklist.Keywords);
    }
}
