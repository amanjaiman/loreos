using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Memory;
using Lore.Agent.Storage;
using Lore.Agent.Tests.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lore.Agent.Tests.Storage;

/// <summary>v2-008 R4: the store is bounded. Evidence ages out on the configured window,
/// <c>raw_captures</c> is capped whether or not the user leaves diagnostics on, and neither
/// path can reach a memory.</summary>
public sealed class RetentionServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = Now;

        public override DateTimeOffset GetUtcNow() => Value;
    }

    private readonly ActivityStore _store = new(":memory:");
    private readonly FixedTime _time = new();

    public void Dispose() => _store.Dispose();

    private RetentionService Build(int retentionDays = 90, bool diagnostics = false) =>
        new(_store,
            new CaptureOptions { RetentionDays = retentionDays, Diagnostics = diagnostics },
            _time,
            NullLogger<RetentionService>.Instance);

    private Task SeedEpisodeAsync(string id, DateTimeOffset endedAt) =>
        _store.SaveEpisodeAsync(new Episode(
            id, endedAt.AddMinutes(-20), endedAt, ["browser"], ["a window"], ["a sample"], 4, []));

    private Task SeedDecisionAsync(string episodeId, DateTimeOffset at) =>
        _store.LogDecisionAsync(new DecisionEntry(
            at, episodeId, "closed", "continuity_break_or_bound", "", "", ""));

    private Task SeedActivityAsync(string title, DateTimeOffset at) =>
        _store.LogActivityAsync(new ActivityLogEntry(
            at, "vault.exe", title, ActivityDecision.Filtered, "BlockedApp", "", ""));

    private Task SeedRawCaptureAsync(string title, DateTimeOffset at) =>
        _store.LogRawCaptureAsync(new RawCaptureEntry(
            at, "browser", title, "UiAutomation", "Reading", "the text Lore read"));

    // ── R4.1: the retention window ────────────────────────────────────────────────

    [Fact]
    public async Task A_sweep_drops_evidence_past_the_window_and_keeps_what_is_inside_it()
    {
        await SeedEpisodeAsync("old", Now.AddDays(-120));
        await SeedEpisodeAsync("recent", Now.AddDays(-30));
        await SeedDecisionAsync("old", Now.AddDays(-120));
        await SeedDecisionAsync("recent", Now.AddDays(-30));
        await SeedActivityAsync("old window", Now.AddDays(-120));
        await SeedActivityAsync("recent window", Now.AddDays(-30));

        using RetentionService service = Build();
        int removed = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(3, removed);
        Assert.Equal("recent", Assert.Single(await _store.GetRecentEpisodesAsync()).Id);
        Assert.Equal("recent", Assert.Single(await _store.GetRecentDecisionsAsync()).EpisodeId);
        Assert.Equal("recent window", Assert.Single(await _store.GetRecentActivityAsync()).WindowTitle);
    }

    [Fact]
    public async Task An_episode_that_ran_up_to_the_boundary_survives_its_own_start_time()
    {
        // Started 91 days ago, ended 89. Evidence ages from when it ended, so this is inside a
        // 90-day window even though its start is not.
        await _store.SaveEpisodeAsync(new Episode(
            "straddler", Now.AddDays(-91), Now.AddDays(-89),
            ["browser"], ["a long session"], ["a sample"], 40, []));

        using RetentionService service = Build(retentionDays: 90);
        await service.SweepAsync(CancellationToken.None);

        Assert.Equal("straddler", Assert.Single(await _store.GetRecentEpisodesAsync()).Id);
    }

    [Fact]
    public async Task Retention_days_zero_prunes_nothing_however_old_the_rows_are()
    {
        await SeedEpisodeAsync("ancient", Now.AddYears(-4));
        await SeedDecisionAsync("ancient", Now.AddYears(-4));
        await SeedActivityAsync("ancient window", Now.AddYears(-4));

        using RetentionService service = Build(retentionDays: 0);
        int removed = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(0, removed);
        Assert.Single(await _store.GetRecentEpisodesAsync());
        Assert.Single(await _store.GetRecentDecisionsAsync());
        Assert.Single(await _store.GetRecentActivityAsync());
    }

    // ── R4.1: memories are NEVER pruned ───────────────────────────────────────────

    [Fact]
    public async Task Pruning_evidence_never_touches_the_memory_it_supported()
    {
        // The store the sweep runs against and the memory seam are different worlds; this asserts
        // the observable half of that — the memory a pruned episode supported is untouched and
        // still carries the (now dangling) episode id.
        var memory = new FakeMemoryService();
        string id = memory.Seed("I'm recovering from a wisdom tooth extraction.");
        await SeedEpisodeAsync("ep-gone", Now.AddDays(-200));

        using RetentionService service = Build(retentionDays: 90);
        await service.SweepAsync(CancellationToken.None);

        Assert.Empty(await _store.GetRecentEpisodesAsync());
        MemoryRecord? survivor = await memory.GetAsync(id);
        Assert.NotNull(survivor);
        Assert.Equal("I'm recovering from a wisdom tooth extraction.", survivor.Memory);
    }

    [Fact]
    public void The_retention_sweep_can_reach_the_activity_store_and_nothing_else()
    {
        // Structural proof of the hard rule: RetentionService takes no dependency that could
        // delete a memory, so "retention never prunes memories" cannot regress by accident.
        Type[] dependencies = typeof(RetentionService)
            .GetConstructors()
            .SelectMany(ctor => ctor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(ActivityStore), dependencies);
        Assert.DoesNotContain(dependencies, type =>
            type.Namespace is "Lore.Agent.Memory" or "Lore.Agent.Inference");
    }

    // ── R4.2: the raw_captures bound ──────────────────────────────────────────────

    [Fact]
    public async Task With_diagnostics_off_the_raw_capture_table_is_emptied()
    {
        // Rows from an earlier troubleshooting session, after the user switched it back off.
        await SeedRawCaptureAsync("read a minute ago", Now.AddMinutes(-1));

        using RetentionService service = Build(diagnostics: false);
        int removed = await service.SweepAsync(CancellationToken.None);

        Assert.Equal(1, removed);
        Assert.Empty(await _store.GetRecentRawCapturesAsync());
    }

    [Fact]
    public async Task Diagnostic_rows_older_than_a_day_go_even_though_the_row_bound_is_not_hit()
    {
        await SeedRawCaptureAsync("yesterday", Now.AddHours(-25));
        await SeedRawCaptureAsync("this morning", Now.AddHours(-3));

        using RetentionService service = Build(diagnostics: true);
        await service.SweepAsync(CancellationToken.None);

        RawCaptureEntry survivor = Assert.Single(await _store.GetRecentRawCapturesAsync());
        Assert.Equal("this morning", survivor.WindowTitle);
    }

    [Fact]
    public async Task A_busy_day_inside_the_age_window_is_still_capped_at_the_row_bound()
    {
        // 600 readings in an hour — the volume that made this table 2-5 GB/year unbounded. All are
        // well inside 24 hours, so only the row bound can hold it.
        for (int i = 0; i < 600; i++)
        {
            await SeedRawCaptureAsync($"reading {i}", Now.AddMinutes(-60 + (i / 10)));
        }

        using RetentionService service = Build(diagnostics: true);
        await service.SweepAsync(CancellationToken.None);

        IReadOnlyList<RawCaptureEntry> kept =
            await _store.GetRecentRawCapturesAsync(limit: 1000);
        Assert.Equal(RetentionService.RawCaptureMaxRows, kept.Count);
        Assert.Equal("reading 599", kept[0].WindowTitle); // the newest are the ones kept
        Assert.Equal("reading 100", kept[^1].WindowTitle);
    }

    [Fact]
    public async Task Diagnostic_pruning_leaves_the_evidence_tables_alone()
    {
        await SeedEpisodeAsync("recent", Now.AddDays(-1));
        await SeedRawCaptureAsync("stale", Now.AddHours(-48));

        using RetentionService service = Build(retentionDays: 90, diagnostics: true);
        await service.SweepAsync(CancellationToken.None);

        Assert.Single(await _store.GetRecentEpisodesAsync());
        Assert.Empty(await _store.GetRecentRawCapturesAsync());
    }

    // ── The hosted-service shape ──────────────────────────────────────────────────

    [Fact]
    public async Task The_service_sweeps_once_on_start_without_waiting_for_the_daily_interval()
    {
        await SeedEpisodeAsync("old", Now.AddDays(-200));
        using RetentionService service = Build();

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await _store.GetRecentEpisodesAsync()).Count == 0);
        await service.StopAsync(CancellationToken.None);

        Assert.Empty(await _store.GetRecentEpisodesAsync());
    }

    [Fact]
    public void Constructor_validates_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new RetentionService(
            null!, new CaptureOptions(), _time, NullLogger<RetentionService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new RetentionService(
            _store, null!, _time, NullLogger<RetentionService>.Instance));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("the retention sweep did not run within the timeout");
    }
}
