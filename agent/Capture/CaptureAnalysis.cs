namespace Lore.Agent.Capture;

/// <summary>The distilled result of analyzing one capture: a clean, first-person
/// <see cref="Observation"/> (the input to mem0 — the quality lever called out in spec
/// 002) and a short <see cref="Category"/>. Never a raw screen dump; if the model can't
/// produce a meaningful observation the analyzer returns <c>null</c> instead.</summary>
public sealed record CaptureAnalysis(string Observation, string Category);
