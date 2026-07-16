namespace Lore.Agent.Distill;

/// <summary>One durable fact a closed episode may support (v2-001). Not yet a memory:
/// the lifecycle engine (T006) decides whether it reinforces, revises, stages, or
/// promotes. <see cref="HorizonDays"/> is the distiller's estimate of how long a
/// <c>state</c> fact stays true; null for every other kind.</summary>
public sealed record CandidateFact(
    string Statement,
    string Kind,
    double Confidence,
    int? HorizonDays);
