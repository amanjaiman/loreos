using Microsoft.Extensions.Logging;

namespace Lore.Agent.Capture.Episodes;

/// <summary>What happens to an episode after it closes: distillation and the memory
/// lifecycle (v2-001 T005/T006). The capture loop persists the episode and its decision
/// trail regardless; this seam only owns the "what durable fact does this support?"
/// tail, so capture keeps running even when no processor is wired.</summary>
public interface IEpisodeProcessor
{
    /// <summary>Process one closed episode. Implementations must not throw for ordinary
    /// failures — a bad model response never kills the capture loop.</summary>
    Task ProcessAsync(Episode episode, CancellationToken cancellationToken = default);
}

/// <summary>Placeholder until the distiller (T005) and lifecycle engine (T006) land:
/// closed episodes are persisted by the loop and simply logged here.</summary>
public sealed class NullEpisodeProcessor : IEpisodeProcessor
{
    private readonly ILogger<NullEpisodeProcessor> _logger;

    public NullEpisodeProcessor(ILogger<NullEpisodeProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task ProcessAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        _logger.LogDebug(
            "episode {Id} closed ({Count} observations); no distiller wired yet",
            episode.Id,
            episode.ObservationCount);
        return Task.CompletedTask;
    }
}
