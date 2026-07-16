using System.IO;
using Lore.Agent.Hosting;
using Lore.Agent.Inference;
using Lore.Agent.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Capture;

/// <summary>Registers the capture pipeline into the 002 host's DI graph (constitution §3.5:
/// constructor injection, no global state). Wires the Win32/UIA seams, the extraction
/// composite, the trust-critical filter, the smart gate, analysis (against a placeholder
/// backend until 004), the local activity store, metrics, and the loop itself.</summary>
public static class CaptureServiceCollectionExtensions
{
    public static IServiceCollection AddCapturePipeline(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        CaptureOptions options = configuration.GetSection("capture").Get<CaptureOptions>() ?? new CaptureOptions();
        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);

        // Win32 / UI Automation seams — the only places that touch the platform.
        services.AddSingleton<IForegroundWindowSource, Win32ForegroundWindowSource>();
        services.AddSingleton<IWindowSecurityProbe, UiaWindowSecurityProbe>();

        // Extraction: UIA primary, OCR fallback, composed behind the one seam.
        services.AddSingleton<UiaTextExtractor>();
        services.AddSingleton<OcrTextExtractor>();
        services.AddSingleton<ITextExtractor>(sp => new CompositeTextExtractor(
            sp.GetRequiredService<UiaTextExtractor>(), sp.GetRequiredService<OcrTextExtractor>()));

        // Trust-critical sensitivity filter.
        services.AddSingleton(new Blocklist(options.BlocklistApps, options.BlocklistKeywords));
        services.AddSingleton<SensitivityFilter>();

        // Smart gate + bounded history.
        services.AddSingleton(options.Gate);
        services.AddSingleton(_ => new RecentCaptureGate(options.Gate.MaxCaptureHistory));
        services.AddSingleton<SmartGate>();

        // Window monitor (dwell threshold from config).
        services.AddSingleton(sp => new WindowMonitor(
            sp.GetRequiredService<IForegroundWindowSource>(),
            sp.GetRequiredService<TimeProvider>(),
            options.DwellThreshold));

        // Analysis — placeholder backend until spec 004 registers the real providers.
        services.AddSingleton<IInferenceBackend, NullInferenceBackend>();
        services.AddSingleton<CaptureAnalyzer>();

        // Local operational store: activity.db next to the memory data dir (NOT mem0).
        services.AddSingleton(sp =>
        {
            MemorydOptions memory = sp.GetRequiredService<IOptions<MemorydOptions>>().Value;
            Directory.CreateDirectory(memory.DataDir);
            return new ActivityStore(Path.Combine(memory.DataDir, "activity.db"));
        });

        // v2 pipeline (behind capture.pipeline): episode segmentation feeding the
        // skeptical distiller and the lifecycle engine.
        services.AddSingleton(options.Episodes);
        services.AddSingleton(options.Lifecycle);
        services.AddSingleton<Episodes.EpisodeBuilder>();
        services.AddSingleton<Distill.Distiller>();
        services.AddSingleton<Episodes.IEpisodeProcessor, Lifecycle.LifecycleEngine>();

        // Metrics, the readiness bridge to memoryd, and the loop.
        services.AddSingleton<CaptureMetrics>();
        services.AddSingleton<IReadinessSignal, MemorydReadinessSignal>();
        services.AddSingleton<CaptureAgent>();
        services.AddHostedService(sp => sp.GetRequiredService<CaptureAgent>());

        return services;
    }
}
