using System.Reflection;
using System.Text.Json.Serialization;
using Lore.Agent.Hosting;
using Lore.Agent.Memory;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

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

        app.MapGet("/system/status", (
            [FromServices] IMemorydReadiness memoryd, [FromServices] ProviderOptions provider) =>
        {
            var components = new ComponentsDto(
                Agent: "running", // if this responds, the agent is up
                Memoryd: memoryd.IsReady ? "ready" : "starting",
                Provider: IsProviderConfigured(provider) ? "ready" : "unconfigured");
            return Results.Ok(new SystemStatusDto(AppVersion(), ApiHost.ApiVersion, components));
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

/// <summary>The system status: app + API version and per-component readiness.</summary>
public sealed record SystemStatusDto(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("api_version")] string ApiVersion,
    [property: JsonPropertyName("components")] ComponentsDto Components);

/// <summary>Per-component readiness. <c>agent</c> is <c>running</c>; <c>memoryd</c> is
/// <c>ready</c>/<c>starting</c>; <c>provider</c> is <c>ready</c>/<c>unconfigured</c>.</summary>
public sealed record ComponentsDto(
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("memoryd")] string Memoryd,
    [property: JsonPropertyName("provider")] string Provider);

/// <summary>The tail of the agent log, oldest line first.</summary>
public sealed record LogTailDto(
    [property: JsonPropertyName("lines")] IReadOnlyList<string> Lines);

/// <summary>The outcome of a data reset: how many memories were removed.</summary>
public sealed record ResetResult(
    [property: JsonPropertyName("deleted")] int Deleted);
