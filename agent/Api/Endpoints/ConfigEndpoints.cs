using System.Text.Json.Nodes;
using Lore.Agent.Config;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The config HTTP surface (spec 005 T004): <c>GET /config</c> and <c>PATCH /config</c>
/// over <see cref="LoreConfig"/>. Responses are scrubbed of inline key material and a PATCH
/// carrying a key routes it to the credential store — so a secret never leaves through, nor is
/// it ever persisted inline (constitution §4.2, acceptance criterion 5).</summary>
public static class ConfigEndpoints
{
    /// <summary>Map the config endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/config", async ([FromServices] LoreConfig config, CancellationToken ct) =>
        {
            JsonObject current = await config.ReadScrubbedAsync(ct).ConfigureAwait(false);
            return Results.Json(current);
        });

        app.MapPatch("/config", async (JsonObject? patch, [FromServices] LoreConfig config, CancellationToken ct) =>
        {
            if (patch is null)
            {
                return Results.BadRequest(new ErrorResponse("a JSON object body is required"));
            }

            JsonObject updated = await config.PatchAsync(patch, ct).ConfigureAwait(false);
            return Results.Json(updated);
        });

        return app;
    }
}
