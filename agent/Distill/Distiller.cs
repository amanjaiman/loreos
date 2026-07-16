using Lore.Agent.Capture.Episodes;
using Lore.Agent.Inference;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Distill;

/// <summary>The one inference call per closed episode (v2-001): ask the user's model
/// what durable fact the episode supports, expecting "none" most of the time. Returns
/// <c>null</c> when the model's output was unusable — the caller records a
/// <c>distill_failed</c> decision and moves on; the loop never crashes on a bad
/// completion.</summary>
public sealed class Distiller
{
    private readonly IInferenceBackend _backend;
    private readonly ILogger<Distiller> _logger;

    public Distiller(IInferenceBackend backend, ILogger<Distiller> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);
        _backend = backend;
        _logger = logger;
    }

    /// <summary>Distill one episode into candidate facts: empty = nothing durable
    /// (the normal case); <c>null</c> = the model call or parse failed.</summary>
    public async Task<IReadOnlyList<CandidateFact>?> DistillAsync(
        Episode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);

        string? raw;
        try
        {
            raw = await _backend
                .CompleteAsync(DistillPrompt.Build(episode), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // a provider outage is a distill_failed decision, not a crash
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(ex, "distillation call failed for episode {Id}", episode.Id);
            return null;
        }

        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(raw);
        if (facts is null)
        {
            _logger.LogWarning("distiller returned unusable output for episode {Id}", episode.Id);
        }

        return facts;
    }
}
