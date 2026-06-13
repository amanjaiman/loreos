using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lore.Agent.Hosting;

/// <summary>Probes memoryd's <c>/health</c> endpoint. Abstracted so the supervisor's
/// health-gate is testable without HTTP.</summary>
public interface IMemorydHealthProbe
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken);
}

/// <summary>Real probe: a <c>GET /health</c> that is green only on
/// <c>{"status":"ok"}</c>.</summary>
public sealed class HttpMemorydHealthProbe : IMemorydHealthProbe
{
    private readonly HttpClient _http;

    public HttpMemorydHealthProbe(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _http = httpClient;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response =
                await _http.GetAsync(new Uri("health", UriKind.Relative), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            JsonElement body = await response.Content
                .ReadFromJsonAsync<JsonElement>(cancellationToken)
                .ConfigureAwait(false);
            return body.TryGetProperty("status", out JsonElement status)
                && status.ValueKind == JsonValueKind.String
                && status.GetString() == "ok";
        }
        catch (HttpRequestException)
        {
            return false; // not up yet
        }
        catch (TaskCanceledException)
        {
            return false; // timed out this poll
        }
        catch (JsonException)
        {
            return false; // a non-JSON body (e.g. a partial/startup response) is not healthy
        }
    }
}
