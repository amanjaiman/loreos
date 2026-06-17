using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lore.Cli;

/// <summary>The CLI's one seam to the local API (constitution §3.1): a <b>thin HTTP client</b> of
/// <c>http://127.0.0.1:7842</c> with no behavior of its own beyond issuing the request and handing
/// back the status code and body. Every command goes through here, so the two cross-cutting
/// concerns the whole CLI shares live in one place: the <b>loopback default</b> and translating a
/// connection failure into <see cref="AgentUnreachableException"/> (the "Lore isn't running" path,
/// acceptance criterion 6). Typed per-command calls are layered on by later tasks; T001 owns the
/// transport.</summary>
internal sealed class ApiClient : IDisposable
{
    /// <summary>The loopback target the CLI talks to unless <c>--api-url</c> overrides it
    /// (constitution §1.1 / §3.4: there is no remote surface).</summary>
    public static readonly Uri DefaultBaseUrl = new("http://127.0.0.1:7842");

    private readonly Uri _baseUrl;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    /// <summary>Create a client against <paramref name="baseUrl"/> (defaulting to loopback). A
    /// caller (or a test) may supply its own <paramref name="httpClient"/> — e.g. one backed by a
    /// fake handler — in which case this type does not dispose it.</summary>
    public ApiClient(Uri? baseUrl = null, HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl ?? DefaultBaseUrl;
        if (httpClient is null)
        {
            _http = new HttpClient();
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttp = false;
        }
    }

    /// <summary>The base URL this client targets — surfaced so the agent-down message can name it.</summary>
    public Uri BaseUrl => _baseUrl;

    /// <summary>Issue a request to <paramref name="path"/> (a server-absolute path like
    /// <c>/memories/search</c>) and return the raw outcome. A connection failure is translated to
    /// <see cref="AgentUnreachableException"/>; every other status is returned verbatim for the
    /// command to map onto an exit code.</summary>
    public async Task<ApiResponse> SendAsync(
        HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUrl, path));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: RequestJson);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (IsConnectionFailure(ex))
        {
            throw new AgentUnreachableException(_baseUrl, ex);
        }

        using (response)
        {
            string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ApiResponse(response.StatusCode, raw);
        }
    }

    /// <summary>Default options for request bodies. Request DTOs carry explicit
    /// <c>JsonPropertyName</c>s (the wire contract is fixed in the DTO, not a serializer default),
    /// so no naming policy is needed here.</summary>
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);

    // "Lore isn't running" surfaces as a connection-level failure. .NET 8 classifies these on
    // HttpRequestException.HttpRequestError; we also unwrap a SocketException for robustness across
    // surfaces (and so tests can simulate it without a live socket).
    private static bool IsConnectionFailure(HttpRequestException ex)
    {
        if (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return true;
        }

        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException socket)
            {
                return socket.SocketErrorCode
                    is SocketError.ConnectionRefused
                    or SocketError.ConnectionReset
                    or SocketError.HostUnreachable
                    or SocketError.HostNotFound
                    or SocketError.TimedOut;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }
}

/// <summary>One raw outcome from the local API: the HTTP status and the response body as text.
/// Commands inspect <see cref="StatusCode"/> to choose an exit code and parse <see cref="Body"/>
/// (JSON for every endpoint except <c>GET /export/markdown</c>).</summary>
internal sealed record ApiResponse(HttpStatusCode StatusCode, string Body)
{
    /// <summary>Whether the status is 2xx.</summary>
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300;

    /// <summary>Parse the body as JSON, or <c>null</c> if it is empty or not JSON.</summary>
    public JsonNode? ReadJson()
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(Body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Thrown when the local API can't be reached — Lore isn't running. Commands catch this
/// and print a friendly, actionable message (never a stack trace) and exit
/// <see cref="ExitCodes.AgentUnreachable"/> (acceptance criterion 6).</summary>
public sealed class AgentUnreachableException : Exception
{
    public AgentUnreachableException()
    {
    }

    public AgentUnreachableException(string message)
        : base(message)
    {
    }

    public AgentUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AgentUnreachableException(Uri baseUrl, Exception inner)
        : base($"could not reach the Lore API at {baseUrl}", inner)
    {
        BaseUrl = baseUrl;
    }

    /// <summary>The API URL the CLI tried to reach.</summary>
    public Uri? BaseUrl { get; }
}
