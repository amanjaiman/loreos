using System.Globalization;
using System.Net;
using Lore.Agent.Api.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace Lore.Agent.Api;

/// <summary>The local API host (constitution §3.1): the one behavior layer every surface —
/// app, MCP server (006), CLI (007) — is a client of. It owns two things the whole API
/// depends on and nothing else should re-decide: the <b>loopback-only bind</b> (§3.4) and the
/// single place endpoint groups are <see cref="MapLoreApi">mapped</see> onto the 002
/// <see cref="WebApplication"/>. Later tasks add their group to <see cref="MapLoreApi"/>; the
/// binding is fixed here and guarded by a fail-fast assertion.</summary>
public static class ApiHost
{
    /// <summary>The loopback interface the API binds to. Never <c>0.0.0.0</c>, <c>+</c>, or an
    /// external address — there is no remote surface (constitution §1.1 / §3.4).</summary>
    public const string LoopbackHost = "127.0.0.1";

    /// <summary>The fixed local API port (constitution §3.1).</summary>
    public const int Port = 7842;

    /// <summary>The API contract version surfaces pin to (plan: OpenAPI <c>info.version</c> and a
    /// status field). Breaking changes bump it; 006/007 build against it.</summary>
    public const string ApiVersion = "1.0";

    /// <summary>The one URL Kestrel listens on.</summary>
    public static readonly string Url =
        string.Create(CultureInfo.InvariantCulture, $"http://{LoopbackHost}:{Port}");

    /// <summary>Pin the host to loopback and fail fast if anything reconfigured it to bind an
    /// external interface. Called once at startup, before <c>Build()</c>.</summary>
    public static void ConfigureLoopbackBinding(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Hard-code the bind so an ASPNETCORE_URLS env var or appsettings entry can't widen
        // it; UseUrls takes precedence over configuration-sourced URLs.
        builder.WebHost.UseUrls(Url);

        // Defense in depth: assert what the host will actually bind. If a future edit (or a
        // host setting we missed) points it at a non-loopback interface, refuse to start
        // rather than silently exposing memory to the network (acceptance criterion 1).
        string? configured = builder.WebHost.GetSetting(WebHostDefaults.ServerUrlsKey);
        EnsureLoopbackOnly(configured);
    }

    /// <summary>The webpack dev-server origin the renderer runs on during <c>npm start</c>
    /// (Electron Forge's webpack plugin default port; nothing in <c>app/forge.config.ts</c>
    /// overrides it). Only reachable in dev.</summary>
    public const string DevRendererOrigin = "http://localhost:3000";

    /// <summary>What Chromium sends as the <c>Origin</c> header for a <c>file://</c> page —
    /// the packaged app's renderer. An opaque origin serializes to the literal string
    /// <c>"null"</c>, not an absent header.</summary>
    private const string FileOriginToken = "null";

    /// <summary>The one CORS policy name, applied globally.</summary>
    public const string CorsPolicyName = "LoreRenderer";

    /// <summary>Register the CORS policy that lets the Electron renderer's <c>fetch</c> calls
    /// actually read the API's responses. The renderer is the one client that runs inside a
    /// browser engine and is therefore subject to CORS at all — the CLI, MCP clients, and
    /// <c>curl</c> send no <c>Origin</c> header, so this middleware is a no-op for them.
    ///
    /// <para>The allow-list is deliberately narrow (<see cref="DevRendererOrigin"/> and the
    /// packaged app's <c>file://</c> origin) rather than <c>AllowAnyOrigin</c>: this API can
    /// read every memory and reset the whole store, and it binds loopback only, so the
    /// meaningful threat is not a remote attacker but any other page already open in the
    /// user's own browser probing <c>127.0.0.1</c> — a known attack class against local
    /// dev-tool APIs. Origin allow-listing is the only gate (constitution: no account, no
    /// auth), so it must stay tight.</para></summary>
    public static void AddCors(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
            .SetIsOriginAllowed(origin =>
                string.Equals(origin, DevRendererOrigin, StringComparison.Ordinal)
                || string.Equals(origin, FileOriginToken, StringComparison.Ordinal))
            .AllowAnyMethod()
            .AllowAnyHeader()));
        // No AllowCredentials(): the renderer never sends cookies/auth, so origin match is the
        // whole gate — and AllowCredentials would only widen the surface for no benefit.
    }

    /// <summary>Apply the CORS policy to the pipeline. Call before endpoints are mapped.</summary>
    public static void UseCors(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseCors(CorsPolicyName);
    }

    /// <summary>The OpenAPI document name, which also fixes its served path: <c>/openapi.json</c>.</summary>
    public const string OpenApiDocumentName = "openapi";

    /// <summary>Register OpenAPI generation. Kept separate from <see cref="MapLoreApi"/> so a host
    /// that only wants the endpoints (e.g. a focused test) need not stand up the document
    /// services. The generated doc is the contract surfaces pin to (acceptance criterion 6).</summary>
    public static void AddOpenApi(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options => options.SwaggerDoc(
            OpenApiDocumentName,
            new OpenApiInfo
            {
                Title = "Lore Local API",
                Version = ApiVersion,
                Description = "The one behavior layer (constitution §3.1): every Lore surface — app, "
                    + "MCP server, CLI — reads and writes through this loopback-only API.",
            }));
    }

    /// <summary>Serve the generated document at <c>/openapi.json</c>. Call after
    /// <see cref="AddOpenApi"/> has registered the services.</summary>
    public static void UseOpenApi(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // RouteTemplate's {documentName} resolves to "openapi", giving exactly "/openapi.json".
        app.UseSwagger(options => options.RouteTemplate = "{documentName}.json");
    }

    /// <summary>Map every endpoint group onto the host. This is the single assembly point the
    /// constitution mandates: each spec-005 task adds its group here, so the contract lives in
    /// one readable place. T001 wires only liveness; T002–T006 add memory, recent/activity,
    /// config, providers, system, and export.</summary>
    public static WebApplication MapLoreApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness: cheap, dependency-free, and what the supervisor/clients poll. Kept distinct
        // from GET /system/status (T005), which reports component readiness.
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapMemoryEndpoints();
        app.MapRecallEndpoints();   // POST /recall — the v2-001 every-turn hot path
        app.MapActivityEndpoints(); // v2-001: episodes, decisions, staging, confirm, economy
        app.MapRecentEndpoints();
        app.MapConfigEndpoints();
        app.MapProviderEndpoints(); // POST /providers/test (from 004)
        app.MapSystemEndpoints();   // /system/status, /system/log, DELETE /system/data
        app.MapExportEndpoints();   // /export/json, /export/markdown

        return app;
    }

    /// <summary>Throw if any configured URL binds to something other than the loopback
    /// interface. Separated from the host wiring so the guarantee is unit-testable against
    /// arbitrary inputs (acceptance criterion 1).</summary>
    public static void EnsureLoopbackOnly(string? serverUrls)
    {
        if (string.IsNullOrWhiteSpace(serverUrls))
        {
            return;
        }

        // ASPNETCORE_URLS is a semicolon-separated list.
        foreach (string raw in serverUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsLoopback(raw))
            {
                throw new InvalidOperationException(
                    $"Refusing to start: the local API may bind loopback only (constitution §3.4), " +
                    $"but '{raw}' targets a non-loopback interface.");
            }
        }
    }

    private static bool IsLoopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            // An unparseable bind string (e.g. "http://+:7842" wildcard host) is not provably
            // loopback, so reject it.
            return false;
        }

        // Named loopback resolves without a DNS round-trip; IP hosts are checked numerically.
        if (uri.HostNameType is UriHostNameType.Dns)
        {
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        }

        return IPAddress.TryParse(uri.Host, out IPAddress? ip) && IPAddress.IsLoopback(ip);
    }
}
