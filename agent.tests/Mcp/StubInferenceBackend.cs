using Lore.Agent.Inference;

namespace Lore.Agent.Tests.Mcp;

/// <summary>A trivial <see cref="IInferenceBackend"/> for the MCP transport tests: it returns a
/// canned completion (or <c>null</c> to model a host with no provider configured) so the tool
/// DI graph is complete without standing up a real provider.</summary>
internal sealed class StubInferenceBackend(string? completion) : IInferenceBackend
{
    public Task<string?> CompleteAsync(InferenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.FromResult(completion);
    }
}
