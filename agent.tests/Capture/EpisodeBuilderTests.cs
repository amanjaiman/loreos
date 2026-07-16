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
        string text = "aftercare instructions for wisdom tooth extraction recovery")
        => new(T0 + TimeSpan.FromMinutes(minutes), exe, title, text, ContentType.Reading);

    private static EpisodeBuilder Builder(EpisodeOptions? options = null) =>
        new(options ?? new EpisodeOptions());

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
    public void Options_with_degenerate_bounds_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Builder(new EpisodeOptions { MaxObservations = 1 }));
        Assert.Throws<ArgumentException>(() => Builder(new EpisodeOptions { MaxAge = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => Builder(new EpisodeOptions { MaxSamples = 1 }));
        Assert.Throws<ArgumentException>(() => Builder(new EpisodeOptions { SampleMaxChars = 0 }));
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
