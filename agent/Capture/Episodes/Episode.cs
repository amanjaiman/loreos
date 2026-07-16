namespace Lore.Agent.Capture.Episodes;

/// <summary>One post-filter observation entering episode segmentation (v2-001). This is
/// the v2 pipeline's unit of intake — already through the sensitivity filter chain, so
/// everything here is safe to store locally and, later, to distill.</summary>
public sealed record CapturedObservation(
    DateTimeOffset At,
    string Executable,
    string Title,
    string Text,
    ContentType ContentType);

/// <summary>A contiguous stretch of related activity — the v2 unit of analysis. Episodes,
/// not individual dwells, go to the distiller; the record is bounded (representative
/// samples, not full text) so one episode can never blow up prompt cost, and it is
/// persisted locally so the user can audit exactly what the distiller saw.</summary>
public sealed record Episode(
    string Id,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    IReadOnlyList<string> Executables,
    IReadOnlyList<string> Titles,
    IReadOnlyList<string> Samples,
    int ObservationCount);
