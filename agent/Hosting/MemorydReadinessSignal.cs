using Lore.Agent.Capture;

namespace Lore.Agent.Hosting;

/// <summary>Bridges the capture loop's <see cref="IReadinessSignal"/> to the memoryd
/// supervisor's <see cref="MemorydSupervisor.Ready"/> health gate (constitution §3.3).</summary>
public sealed class MemorydReadinessSignal : IReadinessSignal
{
    private readonly MemorydSupervisor _supervisor;

    public MemorydReadinessSignal(MemorydSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisor = supervisor;
    }

    public Task WaitUntilReadyAsync(CancellationToken cancellationToken) =>
        _supervisor.Ready.WaitAsync(cancellationToken);
}
