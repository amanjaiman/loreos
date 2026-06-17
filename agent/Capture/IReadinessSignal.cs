namespace Lore.Agent.Capture;

/// <summary>Completes when the memory service is ready to accept observations. Lets the
/// capture loop await memoryd's health gate (spec 002) without depending on the supervisor
/// directly, so the loop is testable with a trivial ready signal.</summary>
public interface IReadinessSignal
{
    /// <summary>Wait until memory is ready, or until cancelled.</summary>
    Task WaitUntilReadyAsync(CancellationToken cancellationToken);
}
