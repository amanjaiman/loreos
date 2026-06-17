using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Lore.Agent.Api.Endpoints;

/// <summary>The provider HTTP surface (spec 004 T005): <c>POST /providers/test</c>. Defined
/// here and mapped onto the 005 API host (and exercised by a thin handler test until that
/// host exists). The app (010) calls it from Settings/onboarding to validate a model config
/// before saving it.</summary>
public static class ProviderEndpoints
{
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Map the provider endpoints onto <paramref name="app"/>.</summary>
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            "/providers/test",
            async (ProviderTestRequest? request, ProviderOptions current, ProviderTester tester, CancellationToken ct) =>
            {
                ProviderTestResult result = await HandleAsync(request, current, tester, ct).ConfigureAwait(false);
                return Results.Json(result, ResponseJson);
            });

        return app;
    }

    /// <summary>The endpoint's logic, separated for direct testing: merge the (optional)
    /// supplied config over the current one, then run the test.</summary>
    public static Task<ProviderTestResult> HandleAsync(
        ProviderTestRequest? request,
        ProviderOptions current,
        ProviderTester tester,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(tester);

        ProviderOptions options = request is null ? current : request.Merge(current);
        return tester.TestAsync(options, cancellationToken);
    }
}

/// <summary>An optional provider config supplied to <c>POST /providers/test</c>. Any field
/// left null falls back to the current configured provider, so the UI can test a single
/// changed field (e.g. a new model) without resending everything.</summary>
public sealed record ProviderTestRequest
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("base_url")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1056:URI-like properties should not be strings",
        Justification = "Raw wire value; validated and normalized to a Uri by ProviderSelector.")]
    public string? BaseUrl { get; init; }

    [JsonPropertyName("api_key_ref")]
    public string? ApiKeyRef { get; init; }

    /// <summary>Overlay this request's set fields onto <paramref name="current"/>.</summary>
    public ProviderOptions Merge(ProviderOptions current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new ProviderOptions
        {
            Type = Type ?? current.Type,
            Model = Model ?? current.Model,
            BaseUrl = BaseUrl ?? current.BaseUrl,
            ApiKeyRef = ApiKeyRef ?? current.ApiKeyRef,
            MaxTokens = current.MaxTokens,
        };
    }
}
