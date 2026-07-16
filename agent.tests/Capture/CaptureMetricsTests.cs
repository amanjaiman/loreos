using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class CaptureMetricsTests
{
    [Fact]
    public void Counts_observations_episodes_and_filter_reasons()
    {
        var metrics = new CaptureMetrics();

        metrics.Observed();
        metrics.Observed();
        metrics.Observed();
        metrics.EpisodeClosed();
        metrics.Filtered(FilterReason.BlockedApp);
        metrics.Filtered(FilterReason.BlockedApp);
        metrics.Filtered(FilterReason.SensitivePattern);

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Equal(3, snapshot.Observed);
        Assert.Equal(1, snapshot.EpisodesClosed);
        Assert.Equal(2, snapshot.Filtered[FilterReason.BlockedApp]);
        Assert.Equal(1, snapshot.Filtered[FilterReason.SensitivePattern]);
        Assert.Equal(3, snapshot.TotalFiltered);
    }

    [Fact]
    public void Zero_counters_are_omitted_from_the_reason_maps()
    {
        var metrics = new CaptureMetrics();
        metrics.Filtered(FilterReason.BlockedApp);

        CaptureMetricsSnapshot snapshot = metrics.Snapshot();
        Assert.Single(snapshot.Filtered);
        Assert.False(snapshot.Filtered.ContainsKey(FilterReason.SensitivePattern));
    }

    [Fact]
    public void Snapshot_is_a_point_in_time_copy()
    {
        var metrics = new CaptureMetrics();
        metrics.Observed();
        CaptureMetricsSnapshot before = metrics.Snapshot();

        metrics.Observed();

        Assert.Equal(1, before.Observed);
        Assert.Equal(2, metrics.Snapshot().Observed);
    }
}
