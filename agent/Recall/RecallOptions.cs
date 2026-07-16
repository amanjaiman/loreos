namespace Lore.Agent.Recall;

/// <summary>The recall hot path's tunables (v2-001), bound from the <c>recall</c> config
/// section. The floor and weights are calibrated against the golden corpus; they are
/// config, not code.</summary>
public sealed class RecallOptions
{
    /// <summary>Default number of memories returned.</summary>
    public int DefaultK { get; init; } = 5;

    /// <summary>How many candidates are fetched per requested result before blending
    /// (over-fetch so kind/temporal weighting can reorder).</summary>
    public int OverFetchMultiplier { get; init; } = 3;

    /// <summary>Blended score below which a hit is dropped. An empty result is the
    /// common, correct case — the floor errs toward empty (spec AC 6).</summary>
    public double Floor { get; init; } = 0.55;

    /// <summary>Recall weight of a current <c>state</c> — the "wisdom teeth" boost.</summary>
    public double StateWeight { get; init; } = 1.15;

    /// <summary>Recall weight of an <c>experience</c> before recency decay.</summary>
    public double ExperienceWeight { get; init; } = 0.9;

    /// <summary>e-folding time of experience recency decay, in days.</summary>
    public double ExperienceDecayDays { get; init; } = 365;

    /// <summary>Experience decay never drops the temporal factor below this.</summary>
    public double ExperienceDecayFloor { get; init; } = 0.6;
}
