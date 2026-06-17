namespace Lore.Agent.Inference;

/// <summary>One prompt for the user's model: a system instruction plus the user content,
/// with a sampling temperature. Deliberately minimal — the capture pipeline only needs a
/// single completion. Spec 004 owns the provider backends; this is the contract capture
/// (003) is built and tested against until 004 lands.</summary>
public sealed record InferenceRequest(string SystemPrompt, string UserPrompt, double Temperature = 0.2);

/// <summary>The single seam to the user's model (constitution §3.2): nothing else in the
/// agent makes a model call. Spec 004 implements the real OpenAI-compatible backends; the
/// capture pipeline depends only on this interface and is developed against a mock.</summary>
public interface IInferenceBackend
{
    /// <summary>Send <paramref name="request"/> to the model and return the raw completion
    /// text, or <c>null</c> when the model produced nothing.</summary>
    Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default);
}
