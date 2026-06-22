using Lore.Agent.Config;
using Lore.Agent.Inference;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Providers;

/// <summary>Wires the provider layer into the host (spec 004 T006): config, secret storage,
/// the backend factory + test-connection service, and the two consumers of the one provider
/// choice — capture analysis (the real <see cref="IInferenceBackend"/>) and memory (the
/// <see cref="Mem0Configurator"/> that reconfigures memoryd). Call after
/// <c>AddCapturePipeline</c> so the real backend replaces its placeholder.</summary>
public static class ProviderServiceCollectionExtensions
{
    public static IServiceCollection AddProviderLayer(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        ProviderOptions options =
            configuration.GetSection("provider").Get<ProviderOptions>() ?? new ProviderOptions();
        services.AddSingleton(options);

        // The optional explicit embedder (spec 013): the escape hatch for providers without
        // first-party embeddings. A mutable singleton, refreshed in place by ProviderReloader
        // so an embedder change through PATCH /config takes effect without a restart.
        EmbedderOptions embedder =
            configuration.GetSection("embedder").Get<EmbedderOptions>() ?? new EmbedderOptions();
        services.AddSingleton(embedder);

        // Secret storage (keys by handle) + the registry that scrubs them from logs.
        services.AddSingleton<SecretRegistry>();
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();

        // The backends' HTTP client — a bounded timeout so a "test connection" against a
        // dead host fails fast instead of hanging.
        services.AddHttpClient(
            ProviderBackendFactory.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IProviderBackendFactory, ProviderBackendFactory>();
        services.AddSingleton<ProviderTester>();

        // Consumer 1 — capture: the real backend replaces the placeholder NullInferenceBackend,
        // wrapped in a reloadable seam so a runtime provider change (PATCH /config) can swap it
        // without restarting the agent. The analyzer holds the stable wrapper; ProviderReloader
        // swaps its inner.
        services.AddSingleton(sp => new ReloadableInferenceBackend(BuildInferenceBackend(sp)));
        services.AddSingleton<IInferenceBackend>(sp => sp.GetRequiredService<ReloadableInferenceBackend>());

        // Consumer 2 — memory: apply the same provider to memoryd once it is healthy. Registered
        // as a resolvable singleton (not just a hosted service) so ProviderReloader can re-invoke
        // ConfigureAsync after the user changes their provider.
        services.AddSingleton<Mem0Configurator>();
        services.AddHostedService(sp => sp.GetRequiredService<Mem0Configurator>());

        // Re-applies a runtime provider change to both consumers above, closing the startup-only
        // wiring gap that otherwise leaves onboarding inert until the next launch.
        services.AddSingleton<IProviderReloader, ProviderReloader>();

        return services;
    }

    /// <summary>Build the capture backend from the current <see cref="ProviderOptions"/>, falling
    /// back to a no-op <see cref="NullInferenceBackend"/> (logging why) when nothing is configured
    /// yet. Shared by startup registration and <see cref="ProviderReloader"/> so both resolve the
    /// provider identically.</summary>
    internal static IInferenceBackend BuildInferenceBackend(IServiceProvider services)
        => BuildInferenceBackend(
            services.GetRequiredService<ProviderOptions>(),
            services.GetRequiredService<IProviderBackendFactory>(),
            services.GetRequiredService<ILoggerFactory>());

    internal static IInferenceBackend BuildInferenceBackend(
        ProviderOptions options, IProviderBackendFactory factory, ILoggerFactory loggerFactory)
    {
        try
        {
            ResolvedProvider resolved = ProviderSelector.Resolve(options);
            return factory.Create(resolved);
        }
        catch (ProviderConfigurationException ex)
        {
            loggerFactory
                .CreateLogger(typeof(ProviderServiceCollectionExtensions).FullName!)
                .LogWarning("No usable provider configured ({Reason}); capture analysis is disabled.", ex.Message);
            return new NullInferenceBackend(loggerFactory.CreateLogger<NullInferenceBackend>());
        }
    }
}
