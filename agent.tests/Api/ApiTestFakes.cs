using Lore.Agent.Hosting;

namespace Lore.Agent.Tests.Api;

/// <summary>A fixed memoryd readiness signal for endpoint tests.</summary>
internal sealed class StubMemorydReadiness : IMemorydReadiness
{
    public StubMemorydReadiness(bool ready) => IsReady = ready;

    public bool IsReady { get; }
}
