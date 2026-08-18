using System.Text.Json.Nodes;
using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;

namespace Lore.Agent.Tests.Capture;

public sealed class EpisodeBuilderTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static CapturedObservation Obs(
        int minutes,
        string exe = "browser",
        string title = "Wisdom tooth aftercare — Clinic",
        string text = "aftercare instructions for wisdom tooth extraction recovery",
        ContentType type = ContentType.Reading)
        => new(T0 + TimeSpan.FromMinutes(minutes), exe, title, text, type);

    private static EpisodeBuilder Builder(EpisodeOptions? options = null) =>
        new(Live(options));

    /// <summary>The builder's thresholds now arrive through the live snapshot (v2-008 R2), so a
    /// test that wants to move them mid-episode holds on to this.</summary>
    private static LiveCaptureSettings Live(EpisodeOptions? options = null) =>
        new(new CaptureOptions { Episodes = options ?? new EpisodeOptions() });

    [Fact]
    public void Related_observations_within_the_gap_join_one_episode()
    {
        EpisodeBuilder builder = Builder();

        Assert.Null(builder.Add(Obs(0)));
        Assert.Null(builder.Add(Obs(1, text: "swelling timeline after extraction and what to eat")));
        Assert.Null(builder.Add(Obs(2, text: "soft foods list for dental recovery week one")));

        Episode episode = builder.Flush()!;
        Assert.Equal(3, episode.ObservationCount);
        Assert.Equal(T0, episode.StartedAt);
        Assert.Equal(T0 + TimeSpan.FromMinutes(2), episode.EndedAt);
    }

    [Fact]
    public void Same_app_keeps_continuity_even_when_content_shifts()
    {
        EpisodeBuilder builder = Builder();

        builder.Add(Obs(0, exe: "code", title: "recall.rs", text: "fn blend(scores)"));
        Episode? closed = builder.Add(Obs(2, exe: "code", title: "budget.rs", text: "completely different tokens"));

        Assert.Null(closed); // same executable is continuity on its own
        Assert.Equal(2, builder.Flush()!.ObservationCount);
    }

    [Fact]
    public void Unrelated_window_closes_the_episode_and_opens_a_new_one()
    {
        EpisodeBuilder builder = Builder();

        builder.Add(Obs(0));
        Episode? closed = builder.Add(
            Obs(1, exe: "game", title: "Chess", text: "queen takes knight on f6"));

        Assert.NotNull(closed);
        Assert.Equal(1, closed.ObservationCount);
        Assert.True(builder.HasOpenEpisode); // the chess observation opened a fresh episode
    }

    [Fact]
    public void A_gap_beyond_continuity_breaks_the_episode_even_for_the_same_app()
    {
        EpisodeBuilder builder = Builder();

        builder.Add(Obs(0));
        Episode? closed = builder.Add(Obs(20)); // same everything, 20 min later

        Assert.NotNull(closed);
        Assert.Equal(1, closed.ObservationCount);
        Assert.Equal(T0, closed.EndedAt); // ended at its own last activity, not the break
    }

    [Fact]
    public void Options_with_degenerate_bounds_are_repaired_rather_than_rejected()
    {
        // These four bounds were constructor arguments checked by throwing until v2-008 R2 made
        // them live; the checks moved to CaptureSnapshot.From, which repairs them to the defaults
        // (asserted in LiveCaptureSettingsTests). What matters here is the consequence: a builder
        // handed a hand-edited config.json full of nonsense still segments normally instead of
        // taking the capture loop down with it.
        EpisodeBuilder builder = Builder(new EpisodeOptions
        {
            MaxObservations = 1,
            MaxAge = TimeSpan.Zero,
            MaxSamples = 1,
            SampleMaxChars = 0,
        });

        Assert.Null(builder.Add(Obs(0)));
        Assert.Null(builder.Add(Obs(1, text: "swelling timeline after extraction and what to eat")));

        Episode episode = builder.Flush()!;
        Assert.Equal(2, episode.ObservationCount); // MaxObservations 1 would have closed at one
        Assert.All(episode.Samples, sample => Assert.NotEmpty(sample)); // SampleMaxChars 0 would empty them
    }

    [Fact]
    public void Idle_timeout_closes_the_open_episode()
    {
        EpisodeBuilder builder = Builder();
        builder.Add(Obs(0));

        Assert.Null(builder.CloseIfIdle(T0 + TimeSpan.FromMinutes(5))); // not idle yet
        Episode? closed = builder.CloseIfIdle(T0 + TimeSpan.FromMinutes(11));

        Assert.NotNull(closed);
        Assert.Equal(T0, closed.EndedAt); // ended when activity stopped, not when noticed
        Assert.False(builder.HasOpenEpisode);
    }

    [Fact]
    public void Observation_cap_closes_the_episode()
    {
        EpisodeBuilder builder = Builder(new EpisodeOptions { MaxObservations = 3 });

        builder.Add(Obs(0));
        builder.Add(Obs(1, text: "aftercare instructions continued, page two"));
        Episode? closed = builder.Add(Obs(2, text: "aftercare instructions continued, page three"));

        Assert.NotNull(closed);
        Assert.Equal(3, closed.ObservationCount); // the capping observation stays inside
        Assert.False(builder.HasOpenEpisode);
    }

    [Fact]
    public void Max_age_closes_the_episode()
    {
        EpisodeBuilder builder = Builder(new EpisodeOptions
        {
            ContinuityGap = TimeSpan.FromMinutes(30),
            MaxAge = TimeSpan.FromMinutes(45),
        });

        Episode? closed = null;
        for (int i = 0; closed is null && i < 60; i += 10)
        {
            closed = builder.Add(Obs(i));
        }

        Assert.NotNull(closed);
        Assert.True(closed.EndedAt - closed.StartedAt >= TimeSpan.FromMinutes(45));
    }

    [Fact]
    public void Near_duplicate_text_is_counted_but_not_sampled()
    {
        EpisodeBuilder builder = Builder();

        builder.Add(Obs(0));
        builder.Add(Obs(1)); // byte-identical text → duplicate
        Episode episode = builder.Flush()!;

        Assert.Equal(2, episode.ObservationCount);
        Assert.Single(episode.Samples);
    }

    [Fact]
    public void Samples_are_bounded_deduplicated_and_keep_first_and_last()
    {
        var options = new EpisodeOptions { MaxSamples = 3, MaxObservations = 100 };
        EpisodeBuilder builder = Builder(options);

        builder.Add(Obs(0, text: "first page about dental surgery basics"));
        for (int i = 1; i < 8; i++)
        {
            builder.Add(Obs(i, text: $"middle page {i} covering topic variant {i} with distinct words w{i}"));
        }

        builder.Add(Obs(9, text: "last page summarizing full recovery outlook"));
        Episode episode = builder.Flush()!;

        Assert.Equal(3, episode.Samples.Count);
        Assert.Contains(episode.Samples, s => s.StartsWith("first page", StringComparison.Ordinal));
        Assert.Contains(episode.Samples, s => s.StartsWith("last page", StringComparison.Ordinal));
    }

    [Fact]
    public void Samples_are_truncated_to_the_configured_length()
    {
        EpisodeBuilder builder = Builder(new EpisodeOptions { SampleMaxChars = 10 });

        builder.Add(Obs(0, text: "0123456789ABCDEF this text is far too long"));
        Episode episode = builder.Flush()!;

        Assert.Equal("0123456789", Assert.Single(episode.Samples));
    }

    // ── v2-008 R6.3: selection weighted by time spent ─────────────────────────────

    // The document below is read for 16 minutes but produces ONE sample, because the
    // re-readings are near-duplicates that Append counts and drops. The chess glance lasts a
    // minute and produces its own sample. Before R6.3 the two were interchangeable and the
    // glance won on novelty alone; the episode's evidence then described the minute, not the
    // quarter of an hour.
    private static EpisodeBuilder SeedTimeDominantEpisode(EpisodeBuilder builder)
    {
        builder.Add(Obs(0, text: "quarterly revenue report opening the document"));
        for (int minute = 1; minute <= 16; minute++)
        {
            // Every reading after the first is byte-identical: counted, never sampled.
            builder.Add(Obs(minute, text: "quarterly revenue report alpha beta gamma delta epsilon zeta"));
        }

        builder.Add(Obs(17, text: "chess queen knight endgame puzzle tactics"));
        builder.Add(Obs(18, text: "weather forecast rain thursday umbrella"));
        return builder;
    }

    [Fact]
    public void A_time_dominant_observation_beats_a_more_novel_glance_for_a_sample_slot()
    {
        // MaxSamples 3: first and last are always kept, so exactly one slot is contested —
        // between the document (16 of the episode's 18 minutes, but 0.75 novelty because its
        // opening line shares wording with the first sample) and the chess glance (one
        // minute, novelty 1.0). Pure diversity picks chess; time-weighted selection does not.
        EpisodeBuilder builder = SeedTimeDominantEpisode(
            Builder(new EpisodeOptions { MaxSamples = 3, MaxObservations = 100 }));

        Episode episode = builder.Flush()!;

        Assert.Equal(3, episode.Samples.Count);
        Assert.Contains(
            episode.Samples, s => s.StartsWith("quarterly revenue report alpha", StringComparison.Ordinal));
        Assert.DoesNotContain(episode.Samples, s => s.StartsWith("chess", StringComparison.Ordinal));

        // Samples stay in chronological order regardless of the order they were scored in.
        Assert.StartsWith("quarterly revenue report opening", episode.Samples[0], StringComparison.Ordinal);
        Assert.StartsWith("weather forecast", episode.Samples[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_measurable_duration_selection_falls_back_to_pure_diversity()
    {
        // Same episode with every observation at the same instant — the degenerate case that
        // only tests and clock skew produce. With no time to weigh by, the most novel
        // candidate wins exactly as it did before R6.3.
        EpisodeBuilder builder = Builder(new EpisodeOptions { MaxSamples = 3, MaxObservations = 100 });
        builder.Add(Obs(0, text: "quarterly revenue report opening the document"));
        builder.Add(Obs(0, text: "quarterly revenue report alpha beta gamma delta epsilon zeta"));
        builder.Add(Obs(0, text: "chess queen knight endgame puzzle tactics"));
        builder.Add(Obs(0, text: "weather forecast rain thursday umbrella"));

        Episode episode = builder.Flush()!;

        Assert.Contains(episode.Samples, s => s.StartsWith("chess", StringComparison.Ordinal));
        Assert.DoesNotContain(
            episode.Samples, s => s.StartsWith("quarterly revenue report alpha", StringComparison.Ordinal));
    }

    // ── v2-008 R6.1: the content-type mix reaches the episode ─────────────────────

    [Fact]
    public void Content_type_mix_counts_every_observation_busiest_kind_first()
    {
        EpisodeBuilder builder = Builder(new EpisodeOptions { MaxObservations = 100 });

        builder.Add(Obs(0, text: "review of the espresso machine grinder burr", type: ContentType.Reading));
        builder.Add(Obs(1, text: "add to cart espresso machine 64mm burr grinder", type: ContentType.Shopping));
        for (int minute = 2; minute <= 7; minute++)
        {
            // Near-duplicates: dropped from the samples, but they are where the time went and
            // so they must still count toward the mix.
            builder.Add(Obs(minute, text: "add to cart espresso machine 64mm burr grinder", type: ContentType.Shopping));
        }

        builder.Add(Obs(8, text: "long form article about coffee extraction", type: ContentType.Reading));
        Episode episode = builder.Flush()!;

        Assert.Equal(9, episode.ObservationCount);
        Assert.Equal(
            [new ContentTypeTally(ContentType.Shopping, 7), new ContentTypeTally(ContentType.Reading, 2)],
            episode.ContentTypeMix);
    }

    [Fact]
    public void Content_type_mix_does_not_leak_across_episodes()
    {
        EpisodeBuilder builder = Builder();
        builder.Add(Obs(0, type: ContentType.Reading));
        Episode first = builder.Add(
            Obs(1, exe: "code", title: "recall.rs", text: "fn blend(scores)", type: ContentType.Coding))!;
        Episode second = builder.Flush()!;

        Assert.Equal([new ContentTypeTally(ContentType.Reading, 1)], first.ContentTypeMix);
        Assert.Equal([new ContentTypeTally(ContentType.Coding, 1)], second.ContentTypeMix);
    }

    // ── v2-008 R2: thresholds are live, and a change never disturbs the open episode ──

    [Fact]
    public void A_threshold_change_mid_episode_applies_from_the_next_observation()
    {
        LiveCaptureSettings settings = Live(new EpisodeOptions { MaxObservations = 100 });
        var builder = new EpisodeBuilder(settings);

        builder.Add(Obs(0));
        builder.Add(Obs(1, text: "swelling timeline after extraction and what to eat"));
        builder.Add(Obs(2, text: "soft foods list for dental recovery week one"));

        // The user drags attentiveness down mid-episode. Three observations are already in.
        settings.Update(new JsonObject
        {
            ["episodes"] = new JsonObject { ["maxObservations"] = 4 },
        });

        // Nothing was force-closed, discarded, or rewritten by the change itself…
        Assert.True(builder.HasOpenEpisode);

        // …and the new cap governs from the very next observation: the fourth closes the episode
        // with all four inside it, exactly as if it had been configured that way from the start.
        Episode? closed = builder.Add(Obs(3, text: "when to switch back to solid food after surgery"));
        Assert.NotNull(closed);
        Assert.Equal(4, closed.ObservationCount);
        Assert.Equal(T0, closed.StartedAt); // the original start survived the change
        Assert.False(builder.HasOpenEpisode);
    }

    [Fact]
    public void A_sample_cap_raised_mid_episode_applies_to_the_episode_that_is_open()
    {
        // The other half of "applies from the next observation": a value only read when the
        // episode closes takes the value in force at that moment, not the one it opened with.
        LiveCaptureSettings settings = Live(new EpisodeOptions { MaxSamples = 2, MaxObservations = 100 });
        var builder = new EpisodeBuilder(settings);

        builder.Add(Obs(0, text: "first page about dental surgery basics"));
        for (int i = 1; i < 6; i++)
        {
            builder.Add(Obs(i, text: $"middle page {i} covering topic variant {i} with distinct words w{i}"));
        }

        settings.Update(new JsonObject
        {
            ["episodes"] = new JsonObject { ["maxSamples"] = 5 },
        });

        Episode episode = builder.Flush()!;
        Assert.Equal(6, episode.ObservationCount); // every observation still there
        Assert.Equal(5, episode.Samples.Count);
    }

    [Fact]
    public void Flush_on_empty_builder_returns_null()
    {
        Assert.Null(Builder().Flush());
        Assert.Null(Builder().CloseIfIdle(T0));
    }

    [Fact]
    public void Episode_ids_are_unique_across_closes()
    {
        EpisodeBuilder builder = Builder();
        builder.Add(Obs(0));
        Episode first = builder.Add(Obs(1, exe: "game", title: "Chess", text: "unrelated"))!;
        Episode second = builder.Flush()!;

        Assert.NotEqual(first.Id, second.Id);
        Assert.StartsWith("ep-", first.Id, StringComparison.Ordinal);
    }
}
