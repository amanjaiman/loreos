using System.Text.Json.Nodes;
using Lore.Agent.Capture;
using Lore.Agent.Config;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The config HTTP surface (spec 005 T004): <c>GET /config</c> and <c>PATCH /config</c>
/// over <see cref="LoreConfig"/>. Responses are scrubbed of inline key material and a PATCH
/// carrying a key routes it to the credential store — so a secret never leaves through, nor is
/// it ever persisted inline (constitution §4.2, acceptance criterion 5).</summary>
public static class ConfigEndpoints
{
    private static readonly SemaphoreSlim PatchGate = new(1, 1);

    /// <summary>Map the config endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/config", async (
            [FromServices] LoreConfig config,
            [FromServices] IServiceProvider services,
            CancellationToken ct) =>
        {
            JsonObject current = await config.ReadScrubbedAsync(ct).ConfigureAwait(false);
            AddResolved(current, services);
            return Results.Json(current);
        });

        app.MapPatch("/config", async (
            JsonObject? patch,
            [FromServices] LoreConfig config,
            [FromServices] IProviderReloader reloader,
            [FromServices] IServiceProvider services,
            CancellationToken ct) =>
        {
            if (patch is null)
            {
                return Results.BadRequest(new ErrorResponse("a JSON object body is required"));
            }

            // Capture whether this patch touches the provider or embedder before the write, so
            // onboarding (and any later model/embedder change) is applied to the live agent rather
            // than waiting for a restart. Capture has its own lightweight snapshot update below.
            bool providerChanged = patch.ContainsKey("provider") || patch.ContainsKey("embedder");

            // capture.resolved is derived, read-only state (v2-008 R3). Dropping it here means the
            // commonest way it could be written back — read GET /config, change one control, send
            // the block back — is a no-op rather than a config file full of pinned numbers that
            // would then override every preset. LoreConfig strips it again before it writes, so
            // this is convenience; that is the guarantee.
            (patch["capture"] as JsonObject)?.Remove(CaptureSnapshot.ResolvedProperty);

            await PatchGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                JsonObject updated = await config.PatchAsync(patch, ct).ConfigureAwait(false);

                if (patch.ContainsKey("capture")
                    && updated["capture"] is JsonObject capture
                    && services.GetService<LiveCaptureSettings>() is { } liveCapture)
                {
                    liveCapture.Update(capture);
                    // A newly blocked or paused current window must disappear from status immediately;
                    // the next enabled capture will repopulate it only after passing the new filter.
                    services.GetService<CaptureStatusTracker>()?.RecordExcluded();
                }

                if (providerChanged)
                {
                    await reloader.ReloadAsync(ct).ConfigureAwait(false);
                }

                // After the write and after the live update, so the caller sees what its own patch
                // resolved to without another round trip — and so nothing added here can be
                // persisted by construction.
                AddResolved(updated, services);
                return Results.Json(updated);
            }
            finally
            {
                PatchGate.Release();
            }
        });

        return app;
    }

    // Hang the effective capture values off capture.resolved (v2-008 R3), echoing the writable
    // keys exactly so the Settings consequence lines can be derived from what is actually in force
    // — including a raw override typed straight into config.json — rather than from a preset name,
    // and so a line can be copied back into `capture` to pin it.
    //
    // Only when the live snapshot is actually there: a host mapping these endpoints without the
    // capture pipeline has no effective values to report, and inventing a `capture` block for it
    // would be a lie.
    private static void AddResolved(JsonObject config, IServiceProvider services)
    {
        if (services.GetService<LiveCaptureSettings>() is not { } liveCapture)
        {
            return;
        }

        if (config["capture"] is not JsonObject capture)
        {
            capture = [];
            config["capture"] = capture;
        }

        capture[CaptureSnapshot.ResolvedProperty] = liveCapture.Current.ToResolvedJson();
    }
}
