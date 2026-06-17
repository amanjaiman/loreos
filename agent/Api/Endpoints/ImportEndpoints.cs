using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The reserved import route (spec 005 T006). The contract surfaces (006/007/010) pin
/// to includes <c>POST /import</c> so it is stable now, but the pipeline that fills it belongs to
/// spec 009 — until then the endpoint exists and answers <c>501 Not Implemented</c> rather than
/// 404, so a client can tell "not built yet" from "wrong URL".</summary>
public static class ImportEndpoints
{
    /// <summary>Map the reserved import placeholder onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost("/import", () => Results.Json(
            new ErrorResponse("import is not implemented yet; reserved for spec 009"),
            statusCode: StatusCodes.Status501NotImplemented));

        return app;
    }
}
