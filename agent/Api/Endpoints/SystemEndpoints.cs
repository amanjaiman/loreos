using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The system HTTP surface (spec 005 T005): <c>GET /system/status</c> (component
/// readiness + version, acceptance criterion 3), <c>GET /system/log</c> (the redacted log tail),
/// and <c>DELETE /system/data</c> (reset the memory set). Status is a cheap, non-blocking
/// snapshot — readiness is read, not probed over the network.</summary>
public static class SystemEndpoints
{
    /// <summary>Map the system endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/system/status", async (
            [FromServices] IMemorydReadiness memoryd, [FromServices] ProviderOptions provider,
            [FromServices] IServiceProvider services, CancellationToken ct) =>
        {
            var components = new ComponentsDto(
                Agent: "running", // if this responds, the agent is up
                Memoryd: memoryd.IsReady ? "ready" : "starting",
                Provider: IsProviderConfigured(provider) ? "ready" : "unconfigured");
            CaptureStatusDto? capture = await ReadCaptureStatusAsync(services, ct).ConfigureAwait(false);
            return Results.Ok(new SystemStatusDto(AppVersion(), ApiHost.ApiVersion, components, capture));
        });

        app.MapGet("/system/log", async ([FromServices] LogTail log, int? lines, CancellationToken ct) =>
        {
            IReadOnlyList<string> tail = await log.ReadAsync(lines, ct).ConfigureAwait(false);
            return Results.Ok(new LogTailDto(tail));
        });

        // Reset: forget every memory. Operational data (activity.db) is the user's audit trail
        // and is left intact; this clears what Lore "knows", not the record of what it did.
        app.MapDelete("/system/data", async ([FromServices] IMemoryService memory, CancellationToken ct) =>
        {
            IReadOnlyList<MemoryRecord> all = await memory.GetAllAsync(cancellationToken: ct).ConfigureAwait(false);
            int deleted = 0;
            foreach (MemoryRecord record in all)
            {
                if (await memory.DeleteAsync(record.Id, ct).ConfigureAwait(false))
                {
                    deleted++;
                }
            }

            return Results.Ok(new ResetResult(deleted));
        });

        return app;
    }

    // The optional capture block (spec 005 R2). Present only when the capture pipeline is wired
    // (it is absent in focused endpoint tests, and clients tolerate its absence — additive, not
    // breaking). window_title carries only a title the filter chain already cleared, and is forced
    // null whenever capture is disabled, so a caller can never infer blocked activity from it.
    private static async Task<CaptureStatusDto?> ReadCaptureStatusAsync(
        IServiceProvider services, CancellationToken ct)
    {
        CaptureStatusTracker? tracker = services.GetService<CaptureStatusTracker>();
        if (tracker is null)
        {
            return null;
        }

        bool enabled = services.GetService<LiveCaptureSettings>()?.Enabled
            ?? await IsCaptureEnabledAsync(services, ct).ConfigureAwait(false);
        CaptureTarget target = tracker.Current;

        // Binding rule 2: a disabled (paused) agent must reveal nothing about the current window,
        // even if the loop has not physically stopped — so the title and timestamp are nulled here
        // rather than trusted to already be clear.
        return enabled
            ? new CaptureStatusDto(true, target.WindowTitle, target.ObservedAt)
            : new CaptureStatusDto(false, null, null);
    }

    // enabled mirrors config.capture.enabled, read live so a PATCH /config pause is reflected at
    // once (the user pauses capture through config). Falls back to the running capture snapshot,
    // then to the capture default (on) when neither the config file nor the snapshot is available.
    private static async Task<bool> IsCaptureEnabledAsync(IServiceProvider services, CancellationToken ct)
    {
        LoreConfig? config = services.GetService<LoreConfig>();
        if (config is not null)
        {
            JsonObject root = await config.ReadScrubbedAsync(ct).ConfigureAwait(false);
            if (root["capture"] is JsonObject capture
                && capture["enabled"] is JsonValue value
                && value.TryGetValue(out bool enabled))
            {
                return enabled;
            }
        }

        return services.GetService<LiveCaptureSettings>()?.Enabled ?? true;
    }

    private static bool IsProviderConfigured(ProviderOptions options)
    {
        try
        {
            ProviderSelector.Resolve(options);
            return true;
        }
        catch (ProviderConfigurationException)
        {
            return false;
        }
    }

    // The app/build version. InformationalVersion carries the friendly value (and may include
    // build metadata after '+', which we trim); fall back to the assembly version.
    private static string AppVersion()
    {
        Assembly assembly = typeof(ApiHost).Assembly;
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}

/// <summary>The system status: app + API version, per-component readiness, and (when the capture
/// pipeline is running) the current capture target. <c>capture</c> is omitted entirely when the
/// pipeline is absent, so this stays additive for clients that predate it (spec 005 R2).</summary>
public sealed record SystemStatusDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("components")] ComponentsDto Components,
    [property: JsonPropertyName("capture")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    CaptureStatusDto? Capture);

/// <summary>Per-component readiness. <c>agent</c> is <c>running</c>; <c>memoryd</c> is
/// <c>ready</c>/<c>starting</c>; <c>provider</c> is <c>ready</c>/<c>unconfigured</c>.</summary>
public sealed record ComponentsDto(
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("memoryd")] string Memoryd,
    [property: JsonPropertyName("provider")] string Provider);

/// <summary>The current capture target for the app rail (spec 005 R2). <c>enabled</c> mirrors
/// config.capture.enabled. <c>window_title</c> is the filter-cleared title of the window Lore is
/// watching, or null when the current window is excluded, capture is disabled, or nothing has been
/// observed yet — a caller can never infer blocked activity from it. Both fields are always
/// present (as null when there is nothing to show) once the block itself is emitted.</summary>
public sealed record CaptureStatusDto(
    [property: JsonPropertyName("enabled")] bool Enabled,
    [property: JsonPropertyName("window_title")] string? WindowTitle,
    [property: JsonPropertyName("observed_at")] DateTimeOffset? ObservedAt);

/// <summary>The tail of the agent log, oldest line first.</summary>
public sealed record LogTailDto(
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines);

/// <summary>The outcome of a data reset: how many memories were removed.</summary>
public sealed record ResetResult(
    [property: JsonPropertyName("deleted")] int Deleted);
