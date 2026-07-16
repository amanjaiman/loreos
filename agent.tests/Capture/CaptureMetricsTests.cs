using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class CaptureMetricsTests
{
    [Fact]
    public void Counts_captures()
    {
        var metrics = new CaptureMetrics();
        metrics.Captured();
        metrics.Captured();

        Assert.Equal(2, metrics.Snapshot().Captured);
    }

    [Fact]
    public void Counts_filtered_drops_per_reason()
    {
        var metrics = new CaptureMetrics();
        metrics.Filtered(FilterReason.BlockedApp);
        metrics.Filtered(FilterReason.BlockedApp);
        metrics.Filtered(FilterReason.SensitivePattern);

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot.Filtered[FilterReason.BlockedApp]);
        Assert.Equal(1, snapshot.Filtered[FilterReason.SensitivePattern]);
        Assert.Equal(3, snapshot.TotalFiltered);
    }

    [Fact]
    public void Counts_skips_per_reason()
    {
        var metrics = new CaptureMetrics();
        metrics.Skipped(SkipReason.Unchanged);
        metrics.Skipped(SkipReason.CodingHeartbeat);
        metrics.Skipped(SkipReason.Unchanged);

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot.Skipped[SkipReason.Unchanged]);
        Assert.Equal(1, snapshot.Skipped[SkipReason.CodingHeartbeat]);
        Assert.Equal(3, snapshot.TotalSkipped);
    }

    [Fact]
    public void Counts_observations_and_closed_episodes_separately_from_captures()
    {
        var metrics = new CaptureMetrics();
        metrics.Observed();
        metrics.Observed();
        metrics.EpisodeClosed();

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(2, snapshot.Observed);
        Assert.Equal(1, snapshot.EpisodesClosed);
        Assert.Equal(0, snapshot.Captured);
    }

    [Fact]
    public void Counts_analysis_empty_and_memory_errors()
    {
        var metrics = new CaptureMetrics();
        metrics.AnalysisEmpty();
        metrics.MemoryError();
        metrics.MemoryError();

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(1, snapshot.AnalysisEmpty);
        Assert.Equal(2, snapshot.MemoryErrors);
    }

    [Fact]
    public void Reasons_never_seen_are_absent_from_the_snapshot()
    {
        var metrics = new CaptureMetrics();
        metrics.Filtered(FilterReason.BlockedApp);

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Single(snapshot.Filtered);
        Assert.DoesNotContain(FilterReason.None, snapshot.Filtered.Keys);
        Assert.Empty(snapshot.Skipped);
    }
}
