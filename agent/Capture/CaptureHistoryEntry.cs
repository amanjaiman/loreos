namespace Lore.Agent.Capture;

/// <summary>A record of one capture the gate let through, kept in bounded recent history
/// so the next candidate can be compared against it. Holds the window identity, the
/// captured text, its content type, and when it happened.</summary>
public sealed record CaptureHistoryEntry(
    long WindowHandle,
    string Title,
    string Text,
    ContentType Type,
    DateTimeOffset At);
