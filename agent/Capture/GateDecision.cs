namespace Lore.Agent.Capture;

/// <summary>Why the smart gate skipped a capture — a typed reason recorded by
/// <c>CaptureMetrics</c> (T008) so the user can see why Lore declined to spend an
/// inference call on a window.</summary>
public enum SkipReason
{
    /// <summary>Not skipped.</summary>
    None,

    /// <summary>The content is too similar to the last capture of this window.</summary>
    Unchanged,

    /// <summary>A near-identical capture is still in the recent-capture window.</summary>
    RecentDuplicate,

    /// <summary>A coding window re-captured within the heartbeat interval — editors churn
    /// constantly and most of that churn isn't worth a memory.</summary>
    CodingHeartbeat,
}

/// <summary>The smart gate's verdict on one capture candidate: capture it, or skip it for
/// a typed reason.</summary>
public sealed record GateDecision(bool ShouldCapture, SkipReason Reason)
{
    /// <summary>Capture this candidate.</summary>
    public static readonly GateDecision Capture = new(true, SkipReason.None);

    /// <summary>Skip this candidate for <paramref name="reason"/>.</summary>
    public static GateDecision Skip(SkipReason reason) => new(false, reason);
}
