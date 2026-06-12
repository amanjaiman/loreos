using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace Lore.Agent.Memory;

/// <summary>The only door to memoryd: a typed HTTP client implementing
/// <see cref="IMemoryService"/>. Every memory call in the agent goes through here, so
/// mem0/HTTP vocabulary never leaks past this seam (constitution §3.2). The
/// <see cref="HttpClient"/>'s <c>BaseAddress</c> (set by DI in spec 002 T005) must
/// point at the local memoryd and end with a trailing slash.</summary>
public sealed class MemorydClient : IMemoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly int _getAllPageSize;

    /// <param name="httpClient">Client whose <c>BaseAddress</c> points at memoryd.</param>
    /// <param name="getAllPageSize">Page size <see cref="GetAllAsync"/> uses to
    /// enumerate a store. Injectable for testing; defaults to 500.</param>
    public MemorydClient(HttpClient httpClient, int getAllPageSize = 500)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(getAllPageSize);
        _http = httpClient;
        _getAllPageSize = getAllPageSize;
    }

    /// <summary>(Re)initialize memoryd's engine from a provider config. Called by the
    /// supervisor once memoryd is healthy (spec 002 T005).</summary>
    public async Task ConfigureAsync(MemoryConfig config, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        using var request = new HttpRequestMessage(HttpMethod.Post, Relative("config"))
        {
            Content = JsonContent.Create(config, options: JsonOptions),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "configure", cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AddedMemory>> RememberAsync(
        string observation,
        string userId = "default",
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(observation);
        var body = new AddRequestBody(observation, userId, metadata);
        Envelope<AddedMemory> result = await PostForJsonAsync<AddRequestBody, Envelope<AddedMemory>>(
            "memories", body, "remember", cancellationToken).ConfigureAwait(false);
        return result.Results ?? [];
    }

    public async Task<IReadOnlyList<MemoryRecord>> SearchAsync(
        string query,
        string userId = "default",
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(query);
        var body = new SearchRequestBody(query, userId, limit);
        Envelope<MemoryRecord> result = await PostForJsonAsync<SearchRequestBody, Envelope<MemoryRecord>>(
            "memories/search", body, "search", cancellationToken).ConfigureAwait(false);
        return result.Results ?? [];
    }

    public Task<IReadOnlyList<MemoryRecord>> GetRecentAsync(
        string userId = "default",
        int count = 20,
        CancellationToken cancellationToken = default) =>
        ListAsync(userId, count, 0, "get_recent", cancellationToken);

    public async Task<IReadOnlyList<MemoryRecord>> GetAllAsync(
        string userId = "default",
        CancellationToken cancellationToken = default)
    {
        // memoryd has no cursor, so page through until a short page signals the end.
        // This enumerates the whole store rather than betting on one huge limit.
        var all = new List<MemoryRecord>();
        for (int offset = 0; ; offset += _getAllPageSize)
        {
            IReadOnlyList<MemoryRecord> page = await ListAsync(
                userId, _getAllPageSize, offset, "get_all", cancellationToken).ConfigureAwait(false);
            all.AddRange(page);
            if (page.Count < _getAllPageSize)
            {
                return all;
            }
        }
    }

    public async Task<MemoryRecord?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var request = new HttpRequestMessage(HttpMethod.Get, Relative($"memories/{Uri.EscapeDataString(id)}"));
        return await SendForRecordOrNullAsync(request, "get", cancellationToken).ConfigureAwait(false);
    }

    public async Task<MemoryRecord?> UpdateAsync(
        string id,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(text);
        using var request = new HttpRequestMessage(HttpMethod.Patch, Relative($"memories/{Uri.EscapeDataString(id)}"))
        {
            Content = JsonContent.Create(new UpdateRequestBody(text), options: JsonOptions),
        };
        return await SendForRecordOrNullAsync(request, "update", cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        using var request = new HttpRequestMessage(HttpMethod.Delete, Relative($"memories/{Uri.EscapeDataString(id)}"));
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, "delete", cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<IReadOnlyList<MemoryRecord>> ListAsync(
        string userId,
        int limit,
        int offset,
        string operation,
        CancellationToken cancellationToken)
    {
        Uri uri = Relative($"memories?user_id={Uri.EscapeDataString(userId)}&limit={limit}&offset={offset}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        Envelope<MemoryRecord> result = await ReadJsonAsync<Envelope<MemoryRecord>>(
            response, operation, cancellationToken).ConfigureAwait(false);
        return result.Results ?? [];
    }

    private async Task<TResponse> PostForJsonAsync<TBody, TResponse>(
        string path,
        TBody body,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Relative(path))
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<TResponse>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MemoryRecord?> SendForRecordOrNullAsync(
        HttpRequestMessage request,
        string operation,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, operation, cancellationToken).ConfigureAwait(false);
        return await ReadJsonAsync<MemoryRecord>(response, operation, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string raw = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        const int BodyCap = 4096;
        string body = raw.Length > BodyCap ? raw[..BodyCap] + " …[truncated]" : raw;
        throw new MemorydException(operation, (int)response.StatusCode, body);
    }

    private static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        T? value = await response.Content
            .ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        return value ?? throw new MemorydException(operation, (int)response.StatusCode, "empty or unparseable body");
    }

    private static Uri Relative(string path) => new(path, UriKind.Relative);

    private readonly record struct Envelope<T>(IReadOnlyList<T> Results);

    private sealed record AddRequestBody(string Text, string UserId, IReadOnlyDictionary<string, object?>? Metadata);

    private sealed record SearchRequestBody(string Query, string UserId, int Limit);

    private sealed record UpdateRequestBody(string Text);
}
