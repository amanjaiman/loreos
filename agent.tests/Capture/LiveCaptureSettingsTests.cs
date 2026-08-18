using System.Text.Json.Nodes;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Tests.Capture;

/// <summary>v2-008 R2: the live snapshot. Every value a preset can move applies on
/// <c>PATCH /config</c> with no agent restart, and nothing a user can type into config.json
/// reaches a consumer in a state that could take the capture loop down.</summary>
public sealed class LiveCaptureSettingsTests
{
    private static LiveCaptureSettings Build(CaptureOptions? options = null) =>
        new(options ?? new CaptureOptions());

    private static JsonObject Capture(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ── The startup snapshot is today's defaults, unchanged ────────────────────────

    [Fact]
    public void An_empty_config_resolves_to_the_shipped_defaults()
    {
        // T003 is plumbing: it must change no behaviour at all until T004 puts presets on top.
        CaptureSnapshot snapshot = Build().Current;
        var defaults = new CaptureOptions();

        Assert.True(snapshot.Enabled);
        Assert.Equal(defaults.PollInterval, snapshot.PollInterval);
        Assert.Equal(defaults.DwellThreshold, snapshot.DwellThreshold);
        Assert.Equal(defaults.RecaptureInterval, snapshot.RecaptureInterval);
        Assert.Equal(defaults.TitleRecaptureInterval, snapshot.TitleRecaptureInterval);
        Assert.Equal(defaults.RetentionDays, snapshot.RetentionDays);
        Assert.False(snapshot.Diagnostics);
        Assert.Equal(new EpisodeOptions(), snapshot.Episodes);
        Assert.Equal(new LifecycleOptions(), snapshot.Lifecycle);
    }

    // ── A PATCH replaces the whole snapshot, atomically ────────────────────────────

    [Fact]
    public void A_patch_applies_every_value_a_preset_will_move()
    {
        LiveCaptureSettings settings = Build();
        settings.Update(Capture(
            """
            {
              "enabled": true,
              "pollInterval": "00:00:05",
              "dwellThreshold": "00:00:10",
              "recaptureInterval": "00:00:40",
              "titleRecaptureInterval": "00:00:15",
              "retentionDays": 30,
              "diagnostics": true,
              "episodes": { "maxObservations": 30, "maxSamples": 6, "sampleMaxChars": 400 },
              "lifecycle": { "highSignalConfidence": 0.9, "stagedTtlDays": 7 }
            }
            """));

        CaptureSnapshot snapshot = settings.Current;
        Assert.Equal(TimeSpan.FromSeconds(5), snapshot.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), snapshot.DwellThreshold);
        Assert.Equal(TimeSpan.FromSeconds(40), snapshot.RecaptureInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), snapshot.TitleRecaptureInterval);
        Assert.Equal(30, snapshot.RetentionDays);
        Assert.True(snapshot.Diagnostics);
        Assert.Equal(30, snapshot.Episodes.MaxObservations);
        Assert.Equal(6, snapshot.Episodes.MaxSamples);
        Assert.Equal(400, snapshot.Episodes.SampleMaxChars);
        Assert.Equal(0.9, snapshot.Lifecycle.HighSignalConfidence);
        Assert.Equal(7, snapshot.Lifecycle.StagedTtlDays);
    }

    [Fact]
    public void The_snapshot_is_swapped_wholesale_never_edited_in_place()
    {
        // The concurrency contract in one assertion: a reader holding the old snapshot keeps a
        // complete, consistent set of values, so it can never observe a half-applied change.
        LiveCaptureSettings settings = Build();
        CaptureSnapshot before = settings.Current;

        settings.Update(Capture("""{ "pollInterval": "00:00:05", "enabled": false }"""));

        Assert.NotSame(before, settings.Current);
        Assert.Equal(TimeSpan.FromSeconds(2), before.PollInterval); // the old view is intact
        Assert.True(before.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.Current.PollInterval);
        Assert.False(settings.Current.Enabled);
    }

    [Fact]
    public void A_patch_that_touches_only_the_provider_leaves_capture_timing_alone()
    {
        // R2's second acceptance. ConfigEndpoints only calls Update when the patch carries a
        // capture block, but the whole capture section arrives when it does, so an untouched key
        // must survive the round trip rather than reverting.
        var settings = Build(new CaptureOptions { PollInterval = TimeSpan.FromSeconds(5) });

        settings.Update(Capture("""{ "blocklistApps": ["1password"] }"""));

        Assert.Equal(TimeSpan.FromSeconds(5), settings.Current.PollInterval);
        Assert.True(settings.Blocklist.MatchesApp("1password.exe"));
    }

    [Fact]
    public void Values_are_read_the_way_the_startup_binder_reads_them()
    {
        // What boots must be what applies live, or a PATCH would silently revert a value the
        // agent honoured at startup. The binder is case-insensitive and takes numbers as strings.
        LiveCaptureSettings settings = Build();
        settings.Update(Capture(
            """
            {
              "PollInterval": "00:00:03",
              "RetentionDays": "45",
              "Episodes": { "MaxObservations": "64" }
            }
            """));

        Assert.Equal(TimeSpan.FromSeconds(3), settings.Current.PollInterval);
        Assert.Equal(45, settings.Current.RetentionDays);
        Assert.Equal(64, settings.Current.Episodes.MaxObservations);
    }

    // ── Nothing in config.json can crash the capture loop ──────────────────────────

    [Fact]
    public void Out_of_range_values_fall_back_to_the_defaults()
    {
        var defaults = new CaptureOptions();
        CaptureSnapshot snapshot = Build(new CaptureOptions
        {
            PollInterval = TimeSpan.Zero,                 // Task.Delay would throw
            DwellThreshold = TimeSpan.FromSeconds(-30),
            RecaptureInterval = TimeSpan.FromSeconds(-1),
            TitleRecaptureInterval = TimeSpan.FromSeconds(-1),
            Episodes = new EpisodeOptions
            {
                MaxObservations = 1,        // an episode with no room for a beginning and an end
                MaxAge = TimeSpan.Zero,     // closes every episode at its first observation
                MaxSamples = 1,
                SampleMaxChars = 0,         // empties every sample; negative throws on truncate
            },
        }).Current;

        Assert.Equal(defaults.PollInterval, snapshot.PollInterval);
        Assert.Equal(defaults.DwellThreshold, snapshot.DwellThreshold);
        Assert.Equal(defaults.RecaptureInterval, snapshot.RecaptureInterval);
        Assert.Equal(defaults.TitleRecaptureInterval, snapshot.TitleRecaptureInterval);
        Assert.Equal(new EpisodeOptions(), snapshot.Episodes);
    }

    [Fact]
    public void A_poll_interval_the_binder_reads_as_days_is_rejected()
    {
        // `"pollInterval": 2` in a hand-edited file: the configuration binder's TimeSpan
        // converter reads a bare 2 as two DAYS. Left alone the loop would poll twice a week and
        // stop closing idle episodes entirely, with nothing in the log to explain it.
        Assert.Equal(
            new CaptureOptions().PollInterval,
            Build(new CaptureOptions { PollInterval = TimeSpan.FromDays(2) }).Current.PollInterval);

        // Just inside the ceiling is honoured — this repairs typos, it does not second-guess a
        // deliberate choice.
        Assert.Equal(
            CaptureSnapshot.MaxPollInterval,
            Build(new CaptureOptions { PollInterval = CaptureSnapshot.MaxPollInterval })
                .Current.PollInterval);
    }

    [Fact]
    public void Unparseable_values_are_ignored_rather_than_read_as_zero()
    {
        var settings = Build(new CaptureOptions { PollInterval = TimeSpan.FromSeconds(5) });

        settings.Update(Capture(
            """
            {
              "pollInterval": "every couple of seconds",
              "retentionDays": "quite a while",
              "episodes": { "maxSamples": true }
            }
            """));

        Assert.Equal(TimeSpan.FromSeconds(5), settings.Current.PollInterval);
        Assert.Equal(90, settings.Current.RetentionDays);
        Assert.Equal(8, settings.Current.Episodes.MaxSamples);
    }

    [Fact]
    public void A_live_patch_of_garbage_is_repaired_the_same_way_as_a_startup_one()
    {
        LiveCaptureSettings settings = Build();

        settings.Update(Capture(
            """{ "pollInterval": "-00:00:02", "episodes": { "maxObservations": 0 } }"""));

        var defaults = new CaptureOptions();
        Assert.Equal(defaults.PollInterval, settings.Current.PollInterval);
        Assert.Equal(defaults.Episodes.MaxObservations, settings.Current.Episodes.MaxObservations);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new LiveCaptureSettings(null!));
        Assert.Throws<ArgumentNullException>(() => Build().Update(null!));
        Assert.Throws<ArgumentNullException>(() => CaptureSnapshot.From(null!));
    }

    // ── One source of truth, structurally ─────────────────────────────────────────

    [Fact]
    public void The_pipeline_registers_no_frozen_copy_of_the_options()
    {
        // The stale-read bug R2 exists to remove can only come back one way: a startup-bound
        // options object left in the container for something to inject. Nothing may resolve
        // CaptureOptions, EpisodeOptions, or LifecycleOptions — the live snapshot is the only
        // way to a capture value.
        var services = new ServiceCollection();
        services.AddCapturePipeline(new ConfigurationBuilder().Build());

        Type[] registered = [.. services.Select(descriptor => descriptor.ServiceType)];
        Assert.Contains(typeof(LiveCaptureSettings), registered);
        Assert.DoesNotContain(typeof(CaptureOptions), registered);
        Assert.DoesNotContain(typeof(EpisodeOptions), registered);
        Assert.DoesNotContain(typeof(LifecycleOptions), registered);
    }
}
