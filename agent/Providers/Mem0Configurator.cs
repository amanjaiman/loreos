using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Providers;

/// <summary>Pushes the user's provider choice into memoryd once it is healthy (spec 004
/// T006): resolve the config, pull the key from the credential store, bridge it to memoryd's
/// <c>POST /config</c>, and apply it. After this runs, the same provider drives both capture
/// analysis and memory. A misconfigured provider is logged and leaves memory unconfigured
/// rather than crashing the host — symmetry with the disabled-capture path.</summary>
public sealed class Mem0Configurator : BackgroundService
{
    private readonly IReadinessSignal _readiness;
    private readonly MemorydClient _memoryd;
    private readonly ProviderOptions _options;
    private readonly ICredentialStore _credentials;
    private readonly string _dataDir;
    private readonly ILogger<Mem0Configurator> _logger;

    public Mem0Configurator(
        IReadinessSignal readiness,
        MemorydClient memoryd,
        ProviderOptions options,
        ICredentialStore credentials,
        IOptions<MemorydOptions> memorydOptions,
        ILogger<Mem0Configurator> logger)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(memoryd);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(memorydOptions);
        ArgumentNullException.ThrowIfNull(logger);
        _readiness = readiness;
        _memoryd = memoryd;
        _options = options;
        _credentials = credentials;
        _dataDir = memorydOptions.Value.DataDir;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _readiness.WaitUntilReadyAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // shutting down before memoryd was ready
        }

        await ConfigureAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <summary>Resolve the provider, bridge it, and apply it to memoryd. Failures are
    /// logged (never with key material) and swallowed so the agent stays up.</summary>
    public async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        try
        {
            ResolvedProvider resolved = ProviderSelector.Resolve(_options);
            string? key = resolved.ApiKeyRef is null ? null : _credentials.Read(resolved.ApiKeyRef);
            MemoryConfig config = Mem0ProviderBridge.ToMemoryConfig(resolved, key, _dataDir);

            await _memoryd.ConfigureAsync(config, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Configured memoryd to use provider {Kind} ({Model}).", resolved.Kind, resolved.Model);
        }
        catch (ProviderConfigurationException ex)
        {
            _logger.LogWarning("Memory left unconfigured: {Reason}", ex.Message);
        }
        catch (MemorydException ex)
        {
            _logger.LogError(ex, "Applying the provider config to memoryd failed.");
        }
    }
}
