using Lore.Agent.Capture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lore.Agent.Import;

/// <summary>Registers the document-import pipeline into the host's DI graph (spec 009 T004).
/// Reuses what the rest of the agent already owns — the trust-critical <see cref="SensitivityFilter"/>
/// (capture, 003) and the <see cref="Lore.Agent.Memory.IMemoryService"/> seam (002) — and adds only
/// the import-specific units. The job store is a singleton (one live view of all jobs a client
/// polls); the service is scoped so it resolves a fresh memory client per background import.</summary>
public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentImport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // TimeProvider is also registered by the capture pipeline; TryAdd keeps this extension
        // usable on its own (e.g. a focused test host that maps only the import endpoints).
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IPdfExtractor, PdfExtractor>();

        // The chunker has constructor defaults DI can't supply, so register a configured instance.
        services.TryAddSingleton(new TextChunker());

        // One shared, in-memory view of every job: the POST that starts one, the GET that polls it,
        // and the background run all see the same store.
        services.TryAddSingleton<ImportJobStore>();

        // Scoped: a background import opens its own DI scope, so the service resolves a per-run
        // memory client rather than capturing a transient one in a singleton.
        services.TryAddScoped<DocumentImportService>();

        return services;
    }
}
