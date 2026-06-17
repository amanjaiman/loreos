using System.Net;
using System.Net.Http;
using Lore.Cli.Commands;

namespace Lore.Cli.Tests;

/// <summary>The read commands (spec 007 T002) drive real <see cref="ApiClient"/> + <see cref="Output"/>
/// over a stubbed API, asserting both human rendering and the exit-code mapping. The <c>--json</c>
/// envelope shape is pinned separately in <see cref="ReadCommandJsonContractTests"/>.</summary>
public sealed class ReadCommandTests
{
    private const string SearchBody =
        "{\"results\":[{\"id\":\"m1\",\"memory\":\"The user is in America/Los_Angeles\","
        + "\"score\":0.82,\"metadata\":{\"category\":\"preferences\"},\"created_at\":\"2026-06-01T10:00:00Z\"}]}";

    private const string PageBody =
        "{\"items\":[{\"id\":\"m1\",\"memory\":\"The user prefers TypeScript\","
        + "\"metadata\":{\"category\":\"coding\"},\"created_at\":\"2026-06-01T10:00:00Z\"}],"
        + "\"total\":7,\"limit\":50,\"offset\":0}";

    private const string MemoryBody =
        "{\"id\":\"m1\",\"memory\":\"The user prefers dark mode\",\"metadata\":{\"category\":\"preferences\"}}";

    private static async Task<(int Code, string Out, string Err)> Run(
        HttpStatusCode status,
        string body,
        bool json,
        Func<ApiClient, Output, CancellationToken, Task<int>> run)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(body),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var output = new Output(stdout, stderr, json);

        int code = await run(client, output, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public async Task Search_renders_hits_in_human_mode()
    {
        (int code, string outText, _) = await Run(HttpStatusCode.OK, SearchBody, json: false,
            (c, o, ct) => SearchCommand.RunAsync(c, o, "timezone", 10, ct));

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("The user is in America/Los_Angeles", outText, StringComparison.Ordinal);
        Assert.Contains("score 0.82", outText, StringComparison.Ordinal);
        Assert.Contains("preferences", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_empty_results_shows_an_empty_message()
    {
        (int code, string outText, _) = await Run(HttpStatusCode.OK, "{\"results\":[]}", json: false,
            (c, o, ct) => SearchCommand.RunAsync(c, o, "nothing", 10, ct));

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("No memories match", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_maps_a_bad_request_to_bad_usage()
    {
        (int code, _, string err) = await Run(HttpStatusCode.BadRequest, "{\"error\":\"query is required\"}", json: false,
            (c, o, ct) => SearchCommand.RunAsync(c, o, string.Empty, 10, ct));

        Assert.Equal(ExitCodes.BadUsage, code);
        Assert.Contains("query is required", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recent_renders_memories_in_human_mode()
    {
        (int code, string outText, _) = await Run(HttpStatusCode.OK, PageBody, json: false,
            (c, o, ct) => RecentCommand.RunAsync(c, o, 20, ct));

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("recent memor", outText, StringComparison.Ordinal);
        Assert.Contains("The user prefers TypeScript", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recent_requests_the_limit_it_was_given()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(PageBody),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var output = new Output(new StringWriter(), new StringWriter(), json: false);

        await RecentCommand.RunAsync(client, output, 5, CancellationToken.None);

        Assert.Contains("/memories?limit=5", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_shows_the_window_and_total()
    {
        (int code, string outText, _) = await Run(HttpStatusCode.OK, PageBody, json: false,
            (c, o, ct) => ListCommand.RunAsync(c, o, 50, 0, ct));

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("of 7", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_renders_a_single_memory()
    {
        (int code, string outText, _) = await Run(HttpStatusCode.OK, MemoryBody, json: false,
            (c, o, ct) => GetCommand.RunAsync(c, o, "m1", ct));

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("The user prefers dark mode", outText, StringComparison.Ordinal);
        Assert.Contains("id m1", outText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_maps_a_missing_id_to_not_found()
    {
        (int code, _, string err) = await Run(HttpStatusCode.NotFound, "{\"error\":\"no memory with id 'x'\"}", json: false,
            (c, o, ct) => GetCommand.RunAsync(c, o, "x", ct));

        Assert.Equal(ExitCodes.NotFound, code);
        Assert.Contains("no memory with id", err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_url_encodes_the_id()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(MemoryBody),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var output = new Output(new StringWriter(), new StringWriter(), json: false);

        await GetCommand.RunAsync(client, output, "a/b", CancellationToken.None);

        // A '/' in the id must be encoded so it can't split into extra path segments.
        Assert.Contains("/memories/a%2Fb", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);
    }
}
