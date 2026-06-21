namespace Lore.Agent.Inference;

/// <summary>A swappable <see cref="IInferenceBackend"/> wrapper. The capture analyzer holds this
/// stable singleton reference, while its inner backend can be replaced at runtime when the user
/// changes their provider through <c>PATCH /config</c> — so onboarding (which always happens after
/// the first launch) takes effect without restarting the agent. The inner field is
/// <see langword="volatile"/> so a swap on the API thread is observed by the capture loop's next
/// call without locking.</summary>
public sealed class ReloadableInferenceBackend : IInferenceBackend
{
    private volatile IInferenceBackend _inner;

    /// <param name="inner">The backend built from the config present at startup; may be a
    /// <see cref="NullInferenceBackend"/> when nothing is configured yet.</param>
    public ReloadableInferenceBackend(IInferenceBackend inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>Replace the backend that future <see cref="CompleteAsync"/> calls forward to.</summary>
    public void Swap(IInferenceBackend inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
        => _inner.CompleteAsync(request, cancellationToken);
}
