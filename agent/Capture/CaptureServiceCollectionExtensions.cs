using System.IO;
using Lore.Agent.Hosting;
using Lore.Agent.Inference;
using Lore.Agent.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Capture;

/// <summary>Registers the capture pipeline into the host's DI graph (constitution §3.5:
/// constructor injection, no global state). Wires the Win32/UIA seams, the extraction
/// composite, the trust-critical filter, episode segmentation, the distiller + lifecycle
/// engine (against a placeholder backend until the provider layer registers the real
/// one), the local activity store, metrics, and the loop itself.</summary>
public static class CaptureServiceCollectionExtensions
{
    public static IServiceCollection AddCapturePipeline(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        CaptureOptions options = configuration.GetSection("capture").Get<CaptureOptions>() ?? new CaptureOptions();
        services.AddSingleton(options);
        services.AddSingleton<LiveCaptureSettings>();
        services.AddSingleton(TimeProvider.System);

        // Win32 / UI Automation seams — the only places that touch the platform.
        services.AddSingleton<IForegroundWindowSource, Win32ForegroundWindowSource>();
        services.AddSingleton<IWindowSecurityProbe, UiaWindowSecurityProbe>();

        // Extraction: UIA primary, OCR fallback, composed behind the one seam. OCR captures
        // pixels via Windows.Graphics.Capture (GPU/fullscreen-capable) and falls back to GDI.
        services.AddSingleton<WgcWindowCapture>();
        services.AddSingleton<UiaTextExtractor>();
        services.AddSingleton<OcrTextExtractor>();
        services.AddSingleton<ITextExtractor>(sp => new CompositeTextExtractor(
            sp.GetRequiredService<UiaTextExtractor>(), sp.GetRequiredService<OcrTextExtractor>()));

        // Trust-critical sensitivity filter.
        services.AddSingleton<SensitivityFilter>();

        // Window monitor (dwell threshold from config).
        services.AddSingleton(sp => new WindowMonitor(
            sp.GetRequiredService<IForegroundWindowSource>(),
            sp.GetRequiredService<TimeProvider>(),
            options.DwellThreshold));

        // Placeholder backend until the provider layer (004) registers the real one.
        services.AddSingleton<IInferenceBackend, NullInferenceBackend>();

        // Local operational store: activity.db next to the memory data dir (NOT mem0).
        services.AddSingleton(sp =>
        {
            MemorydOptions memory = sp.GetRequiredService<IOptions<MemorydOptions>>().Value;
            Directory.CreateDirectory(memory.DataDir);
            return new ActivityStore(Path.Combine(memory.DataDir, "activity.db"));
        });

        // Keeps that store bounded (v2-008 R4): prunes evidence past capture.retentionDays and
        // holds the opt-in raw_captures diagnostic to its 24h/500-row ceiling, on start and daily.
        // Registered here, next to the store it sweeps, so the two can never be wired apart.
        services.AddHostedService<RetentionService>();

        // Episode segmentation feeding the skeptical distiller and the lifecycle engine.
        services.AddSingleton(options.Episodes);
        services.AddSingleton(options.Lifecycle);
        services.AddSingleton<Episodes.EpisodeBuilder>();
        services.AddSingleton<Distill.Distiller>();
        services.AddSingleton<Episodes.IEpisodeProcessor, Lifecycle.LifecycleEngine>();

        // The live "what is Lore watching" signal the app rail reads via GET /system/status (R2).
        services.AddSingleton<CaptureStatusTracker>();

        // Metrics, the readiness bridge to memoryd, and the loop.
        services.AddSingleton<CaptureMetrics>();
        services.AddSingleton<IReadinessSignal, MemorydReadinessSignal>();
        services.AddSingleton<CaptureAgent>();
        services.AddHostedService(sp => sp.GetRequiredService<CaptureAgent>());

        return services;
    }
}
