namespace Lore.Agent.Capture.Episodes;

/// <summary>What happens to an episode after it closes: distillation and the memory
/// lifecycle (the v2-001 <c>LifecycleEngine</c>). The capture loop persists the episode
/// and its decision trail regardless; this seam only owns the "what durable fact does
/// this support?" tail, so capture keeps running even when processing fails.</summary>
public interface IEpisodeProcessor
{
    /// <summary>Process one closed episode. Implementations must not throw for ordinary
    /// failures — a bad model response never kills the capture loop.</summary>
    Task ProcessAsync(Episode episode, CancellationToken cancellationToken = default);
}
