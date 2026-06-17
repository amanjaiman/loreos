using System.Diagnostics;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>Runs a one-shot "test connection" for a provider config (spec 004 T005): resolve
/// the config, build the backend, send a tiny prompt, and report success with latency or a
/// specific, actionable failure. The host's <c>POST /providers/test</c> endpoint is a thin
/// wrapper over this; keeping the logic here makes every path unit-testable without HTTP.</summary>
public sealed class ProviderTester
{
    // The probe asks for almost nothing — connectivity and auth are what we're testing, not
    // generation — so the call stays cheap regardless of the configured max tokens.
    private const int ProbeMaxTokens = 16;
    private static readonly InferenceRequest Probe = new(string.Empty, "Reply with the single word: OK.");

    private readonly IProviderBackendFactory _factory;

    public ProviderTester(IProviderBackendFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task<ProviderTestResult> TestAsync(
        ProviderOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        ResolvedProvider resolved;
        IInferenceBackend backend;
        try
        {
            resolved = ProviderSelector.Resolve(options);
            ResolvedProvider probe = resolved with { MaxTokens = Math.Min(resolved.MaxTokens, ProbeMaxTokens) };
            backend = _factory.Create(probe);
        }
        catch (ProviderConfigurationException ex)
        {
            return ProviderTestResult.Fail("configuration", ex.Message);
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        try
        {
            await backend.CompleteAsync(Probe, cancellationToken).ConfigureAwait(false);
            long elapsedMs = (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            return ProviderTestResult.Success(resolved.Model, elapsedMs);
        }
        catch (ProviderException ex)
        {
            return ProviderTestResult.Fail(ex.Kind.ToString(), ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ProviderTestResult.Fail(
                nameof(ProviderErrorKind.Unreachable), "The connection test was canceled or timed out.");
        }
    }
}
