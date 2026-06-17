using Lore.Agent.Inference;

namespace Lore.Agent.Capture;

/// <summary>Distills a filtered capture into a first-person observation by asking the
/// user's model (through the <see cref="IInferenceBackend"/> seam) and parsing its reply.
/// This is what feeds mem0 — a clean observation, not a raw screen dump (spec 002's quality
/// lever). Malformed model output is handled by the parser returning <c>null</c> (skip);
/// backend/transport failures propagate to the loop, which owns resilience (T008).</summary>
public sealed class CaptureAnalyzer
{
    private readonly IInferenceBackend _backend;

    public CaptureAnalyzer(IInferenceBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backend = backend;
    }

    /// <summary>Analyze a filtered title and text. Returns the distilled observation, or
    /// <c>null</c> when there is nothing to analyze or the model produced no usable
    /// result.</summary>
    public async Task<CaptureAnalysis?> AnalyzeAsync(
        string? title, string? text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(text))
        {
            return null; // nothing to distill — don't spend a model call
        }

        InferenceRequest request = AnalysisPrompt.Build(title, text);
        string? raw = await _backend.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        return ObservationParser.Parse(raw);
    }
}
