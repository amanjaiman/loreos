namespace Lore.Agent.Hosting;

/// <summary>A synchronous read of whether memoryd has reached its health gate — what
/// <c>GET /system/status</c> reports (spec 005 T005). Distinct from the capture loop's
/// <c>IReadinessSignal</c> (which awaits readiness) and from <c>IMemorydHealthProbe</c> (which
/// pings memoryd over HTTP): status wants a cheap, non-blocking snapshot, and an interface keeps
/// it fakeable without standing up a real supervisor.</summary>
public interface IMemorydReadiness
{
    /// <summary><c>true</c> once memoryd has become healthy since startup.</summary>
    bool IsReady { get; }
}

/// <summary>Reports readiness from the supervisor's <see cref="MemorydSupervisor.Ready"/> gate
/// without blocking: the gate's task completes successfully exactly when memoryd first went
/// healthy.</summary>
public sealed class SupervisorReadiness : IMemorydReadiness
{
    private readonly MemorydSupervisor _supervisor;

    public SupervisorReadiness(MemorydSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisor = supervisor;
    }

    public bool IsReady => _supervisor.Ready.IsCompletedSuccessfully;
}
