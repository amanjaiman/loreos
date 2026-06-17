using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lore.Agent.Inference;

namespace Lore.Agent.Providers;

/// <summary>Shared HTTP plumbing for the model backends (spec 004): POST a JSON body,
/// map transport/status failures to a categorized <see cref="ProviderException"/>, and
/// hand the parsed JSON to the concrete backend to extract the completion. Each provider
/// differs only in its endpoint, auth header, request shape, and response shape — the
/// template methods below — so the error handling lives in one place
/// (constitution §3.2: one seam, one set of rules).</summary>
public abstract class HttpInferenceBackend : IInferenceBackend
{
    /// <summary>Serializer for request bodies. Backends name anonymous-object properties
    /// exactly as the wire expects (e.g. <c>max_tokens</c>), so no naming policy is needed;
    /// nulls are dropped so optional fields (a missing system prompt) simply vanish.</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private const int ErrorBodyCap = 2048;

    private readonly HttpClient _http;

    protected HttpInferenceBackend(HttpClient httpClient, string model, int maxTokens)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTokens);
        _http = httpClient;
        Model = model;
        MaxTokens = maxTokens;
    }

    /// <summary>The model id this backend calls.</summary>
    protected string Model { get; }

    /// <summary>Upper bound on generated tokens per call.</summary>
    protected int MaxTokens { get; }

    /// <summary>Absolute URL the request is POSTed to.</summary>
    protected abstract Uri Endpoint { get; }

    /// <summary>A short host label for error messages (never includes credentials).</summary>
    protected virtual string EndpointLabel => Endpoint.Host;

    /// <summary>Apply provider-specific auth/version headers to the request.</summary>
    protected abstract void ApplyHeaders(HttpRequestMessage request);

    /// <summary>Build the provider-specific JSON request body.</summary>
    protected abstract object BuildRequestBody(InferenceRequest request);

    /// <summary>Pull the completion text out of the provider's JSON response, or
    /// <c>null</c> when it produced none.</summary>
    protected abstract string? ExtractCompletion(JsonElement root);

    public async Task<string?> CompleteAsync(
        InferenceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(BuildRequestBody(request), options: JsonOptions),
        };
        ApplyHeaders(httpRequest);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new ProviderException(
                ProviderErrorKind.Unreachable, $"Could not reach {EndpointLabel}: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProviderException(
                ProviderErrorKind.Unreachable, $"Request to {EndpointLabel} timed out.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw await BuildStatusExceptionAsync(response, cancellationToken).ConfigureAwait(false);
            }

            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using JsonDocument document = JsonDocument.Parse(body);
                string? text = ExtractCompletion(document.RootElement);
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (JsonException ex)
            {
                throw new ProviderException(
                    ProviderErrorKind.BadResponse, $"{EndpointLabel} returned an unparseable response.", ex);
            }
        }
    }

    private async Task<ProviderException> BuildStatusExceptionAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        string body = raw.Length > ErrorBodyCap ? raw[..ErrorBodyCap] : raw;
        int status = (int)response.StatusCode;

        // Order matters: some providers (Gemini) report a bad key as HTTP 400 with an
        // "API key" message rather than 401, so the key check comes before the rest.
        ProviderErrorKind kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.Unauthorized,
            HttpStatusCode.BadRequest when MentionsApiKey(body) => ProviderErrorKind.Unauthorized,
            HttpStatusCode.NotFound => ProviderErrorKind.ModelNotFound,
            HttpStatusCode.BadRequest when MentionsModel(body) => ProviderErrorKind.ModelNotFound,
            _ => ProviderErrorKind.BadResponse,
        };

        string message = kind switch
        {
            ProviderErrorKind.Unauthorized =>
                $"{EndpointLabel} rejected the API key (HTTP {status}). Check the key for this provider.",
            ProviderErrorKind.ModelNotFound =>
                $"{EndpointLabel} does not recognize model '{Model}' (HTTP {status}). Check provider.model.",
            _ => $"{EndpointLabel} returned HTTP {status}: {Summarize(body)}",
        };

        return new ProviderException(kind, message);
    }

    private static bool MentionsModel(string body) =>
        body.Contains("model", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsApiKey(string body) =>
        body.Contains("api key", StringComparison.OrdinalIgnoreCase)
        || body.Contains("api_key", StringComparison.OrdinalIgnoreCase);

    private static string Summarize(string body)
    {
        string trimmed = body.Trim();
        return trimmed.Length == 0 ? "(no body)" : trimmed;
    }
}
