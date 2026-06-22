using Lore.Agent.Config;
using Lore.Agent.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Providers;

/// <summary>The seam <c>PATCH /config</c> calls to push a provider change into the live agent.
/// An interface so the config endpoint depends on the behavior, not the concrete graph — tests
/// substitute a no-op (or a counting stub) without standing up the whole provider layer.</summary>
public interface IProviderReloader
{
    /// <summary>Re-read the persisted provider config and apply it to the capture backend and
    /// memoryd. Idempotent; safe to call after any config change.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

/// <summary>Re-applies the user's provider choice to the already-running agent after it changes
/// through <c>PATCH /config</c>. Without this, the provider is bound only at startup, so a fresh
/// install — where the user always onboards <em>after</em> the first launch — leaves capture
/// analysis disabled and memoryd unconfigured until the app is restarted.
///
/// <para>It re-reads the <c>provider</c> block from config.json exactly as startup binds it,
/// updates the shared <see cref="ProviderOptions"/> singleton <b>in place</b> (so
/// <see cref="Mem0Configurator"/>, which holds that same reference, sees the new values), rebuilds
/// the capture backend behind the <see cref="ReloadableInferenceBackend"/> seam, and re-pushes the
/// provider to memoryd. Reloads are serialized; failures are logged and swallowed (symmetry with
/// the startup path) so a bad config never takes the host down.</para></summary>
public sealed class ProviderReloader : IProviderReloader, IDisposable
{
    private readonly ProviderOptions _options;
    private readonly EmbedderOptions _embedder;
    private readonly IProviderBackendFactory _factory;
    private readonly ReloadableInferenceBackend _backend;
    private readonly Mem0Configurator _memory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProviderReloader> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProviderReloader(
        ProviderOptions options,
        EmbedderOptions embedder,
        IProviderBackendFactory factory,
        ReloadableInferenceBackend backend,
        Mem0Configurator memory,
        ILoggerFactory loggerFactory,
        ILogger<ProviderReloader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(embedder);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _embedder = embedder;
        _factory = factory;
        _backend = backend;
        _memory = memory;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>Re-read the persisted provider config and apply it to both the capture backend and
    /// memoryd. Safe to call after any config change; it is a no-op-cost rebuild when the provider
    /// is unchanged.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Bind the provider + embedder blocks straight from the file the PATCH just wrote —
            // the same sections and binder startup uses, so a reload and a relaunch resolve
            // identically.
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddJsonFile(LoreConfig.DefaultPath, optional: true, reloadOnChange: false)
                .Build();
            ProviderOptions fresh = config.GetSection("provider").Get<ProviderOptions>() ?? new ProviderOptions();
            EmbedderOptions freshEmbedder = config.GetSection("embedder").Get<EmbedderOptions>() ?? new EmbedderOptions();

            // Mutate the shared singletons in place: Mem0Configurator captured these very
            // instances, so updating their fields is what makes the memoryd reconfigure below
            // pick up the change.
            _options.Type = fresh.Type;
            _options.Model = fresh.Model;
            _options.BaseUrl = fresh.BaseUrl;
            _options.ApiKeyRef = fresh.ApiKeyRef;
            _options.MaxTokens = fresh.MaxTokens;

            _embedder.Type = freshEmbedder.Type;
            _embedder.Model = freshEmbedder.Model;
            _embedder.BaseUrl = freshEmbedder.BaseUrl;
            _embedder.ApiKeyRef = freshEmbedder.ApiKeyRef;
            _embedder.Dims = freshEmbedder.Dims;

            _backend.Swap(ProviderServiceCollectionExtensions.BuildInferenceBackend(
                _options, _factory, _loggerFactory));

            // memoryd was health-gated at startup; if it is up this configures it, if not it logs
            // and moves on (Mem0Configurator owns that resilience).
            await _memory.ConfigureAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Provider reloaded from config (type {Type}, model {Model}).",
                string.IsNullOrEmpty(_options.Type) ? "<none>" : _options.Type,
                string.IsNullOrEmpty(_options.Model) ? "<none>" : _options.Model);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
