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

        app.MapGet("/config", async ([FromServices] LoreConfig config, CancellationToken ct) =>
        {
            JsonObject current = await config.ReadScrubbedAsync(ct).ConfigureAwait(false);
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

                return Results.Json(updated);
            }
            finally
            {
                PatchGate.Release();
            }
        });

        return app;
    }
}
