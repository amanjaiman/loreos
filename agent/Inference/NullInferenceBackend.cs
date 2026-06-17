using Microsoft.Extensions.Logging;

namespace Lore.Agent.Inference;

/// <summary>A placeholder backend used until spec 004 wires the real OpenAI-compatible
/// providers. It produces no completion, so capture analysis is effectively disabled and
/// the loop stores nothing — it just logs once that no model is configured. The capture
/// pipeline is otherwise fully wired and tested against a mock; replacing this registration
/// is all 004 needs to do to light it up.</summary>
public sealed class NullInferenceBackend : IInferenceBackend
{
    private readonly ILogger<NullInferenceBackend> _logger;
    private int _warned;

    public NullInferenceBackend(ILogger<NullInferenceBackend> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _logger.LogWarning(
                "No inference backend is configured (spec 004 not yet wired); capture analysis is disabled.");
        }

        return Task.FromResult<string?>(null);
    }
}
