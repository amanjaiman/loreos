namespace Lore.Agent.Lifecycle;

/// <summary>Lifecycle routing thresholds (v2-001), bound from the
/// <c>capture.lifecycle</c> config section. The similarity bands are calibrated by the
/// golden corpus (T007); they are config, not code.</summary>
public sealed class LifecycleOptions
{
    /// <summary>Search score at or above which a candidate IS an existing memory
    /// (reinforce / promote-from-staged).</summary>
    public double SameFactThreshold { get; init; } = 0.90;

    /// <summary>Search score at or above which a candidate is about the same topic as an
    /// existing memory — LLM arbitration decides duplicate / supersedes / coexist.</summary>
    public double SameTopicThreshold { get; init; } = 0.75;

    /// <summary>Distiller confidence at or above which an unmatched candidate promotes
    /// directly (a committed action like a booking), skipping staging.</summary>
    public double HighSignalConfidence { get; init; } = 0.85;

    /// <summary>Runaway guard: max promotions per UTC day. At the cap candidates stay
    /// staged (deferred, not dropped).
    ///
    /// This is a circuit breaker, NOT an allowance. It exists so a misbehaving distiller
    /// — a bad prompt, a model that starts inventing facts — cannot flood active memory
    /// in a single day; the overflow lands in staging where it is visible and reversible.
    /// It was never meant to ration how much Lore may learn about someone, and at the old
    /// default of 10 it did exactly that: an ordinary busy day hit the cap, real memories
    /// were deferred, and the user was handed a judgment queue for no reason. The value is
    /// now set well above any plausible real day, so it only ever trips on a fault.</summary>
    public int DailyBudget { get; init; } = 250;

    /// <summary>Staged candidates expire after this many days without support.</summary>
    public int StagedTtlDays { get; init; } = 14;

    /// <summary>Horizon for a <c>state</c> fact when the distiller offers none.</summary>
    public int DefaultHorizonDays { get; init; } = 45;

    /// <summary>Distiller horizons are clamped into this range (days).</summary>
    public int MinHorizonDays { get; init; } = 7;

    public int MaxHorizonDays { get; init; } = 180;

    /// <summary>Confidence bump applied on reinforcement/promotion.</summary>
    public double ReinforceBump { get; init; } = 0.1;

    /// <summary>Inferred confidence never exceeds this (1.0 is reserved for the user).</summary>
    public double ConfidenceCap { get; init; } = 0.95;

    /// <summary>How many neighbors the candidate is matched against.</summary>
    public int MatchNeighbors { get; init; } = 5;
}
