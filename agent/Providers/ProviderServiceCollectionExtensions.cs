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

        // Secret storage (keys by handle) + the registry that scrubs them from logs.
        services.AddSingleton<SecretRegistry>();
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();

        // The backends' HTTP client — a bounded timeout so a "test connection" against a
        // dead host fails fast instead of hanging.
        services.AddHttpClient(
            ProviderBackendFactory.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<IProviderBackendFactory, ProviderBackendFactory>();
        services.AddSingleton<ProviderTester>();

        // Consumer 1 — capture: the real backend replaces the placeholder NullInferenceBackend.
        services.AddSingleton<IInferenceBackend>(BuildInferenceBackend);

        // Consumer 2 — memory: apply the same provider to memoryd once it is healthy.
        services.AddHostedService<Mem0Configurator>();

        return services;
    }

    private static IInferenceBackend BuildInferenceBackend(IServiceProvider services)
    {
        ProviderOptions options = services.GetRequiredService<ProviderOptions>();
        try
        {
            ResolvedProvider resolved = ProviderSelector.Resolve(options);
            return services.GetRequiredService<IProviderBackendFactory>().Create(resolved);
        }
        catch (ProviderConfigurationException ex)
        {
            services.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(ProviderServiceCollectionExtensions).FullName!)
                .LogWarning("No usable provider configured ({Reason}); capture analysis is disabled.", ex.Message);
            return new NullInferenceBackend(services.GetRequiredService<ILogger<NullInferenceBackend>>());
        }
    }
}
