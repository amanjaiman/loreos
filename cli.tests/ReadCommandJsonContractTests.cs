using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Lore.Cli.Commands;

namespace Lore.Cli.Tests;

/// <summary>The <c>--json</c> contract for read commands (spec 007, acceptance criterion 2): every
/// command emits exactly one envelope — <c>{ schema_version, ok, data, error }</c> — and the
/// <c>data</c> mirrors the local API's shape (<c>results</c> for search, the paged
/// <c>items/total/limit/offset</c> for recent/list, the memory object for get). These field names
/// are a stable machine contract a calling agent can pin to; a change here is a breaking change.</summary>
public sealed class ReadCommandJsonContractTests
{
    private static async Task<JsonNode> RunJson(
        string apiBody, Func<ApiClient, Output, CancellationToken, Task<int>> run)
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(apiBody),
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        int code = await run(client, output, CancellationToken.None);
        Assert.Equal(ExitCodes.Success, code);
        return JsonNode.Parse(stdout.ToString())!;
    }

    private static void AssertOkEnvelope(JsonNode envelope)
    {
        Assert.Equal("1.0", envelope["schema_version"]!.GetValue<string>());
        Assert.True(envelope["ok"]!.GetValue<bool>());
        Assert.Null(envelope["error"]);
        Assert.NotNull(envelope["data"]);
    }

    [Fact]
    public async Task Search_envelope_exposes_results()
    {
        JsonNode envelope = await RunJson(
            "{\"results\":[{\"id\":\"m1\",\"memory\":\"x\",\"score\":0.5}]}",
            (c, o, ct) => SearchCommand.RunAsync(c, o, "q", 10, ct));

        AssertOkEnvelope(envelope);
        JsonNode first = envelope["data"]!["results"]!.AsArray()[0]!;
        Assert.Equal("m1", first["id"]!.GetValue<string>());
        Assert.Equal("x", first["memory"]!.GetValue<string>());
        Assert.Equal(0.5, first["score"]!.GetValue<double>());
    }

    [Fact]
    public async Task Recent_envelope_exposes_the_paged_items()
    {
        JsonNode envelope = await RunJson(
            "{\"items\":[{\"id\":\"m1\",\"memory\":\"x\"}],\"total\":1,\"limit\":20,\"offset\":0}",
            (c, o, ct) => RecentCommand.RunAsync(c, o, 20, ct));

        AssertOkEnvelope(envelope);
        JsonNode data = envelope["data"]!;
        Assert.Equal("m1", data["items"]!.AsArray()[0]!["id"]!.GetValue<string>());
        Assert.Equal(1, data["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task List_envelope_exposes_the_paging_window()
    {
        JsonNode envelope = await RunJson(
            "{\"items\":[],\"total\":0,\"limit\":50,\"offset\":10}",
            (c, o, ct) => ListCommand.RunAsync(c, o, 50, 10, ct));

        AssertOkEnvelope(envelope);
        JsonNode data = envelope["data"]!;
        Assert.Equal(50, data["limit"]!.GetValue<int>());
        Assert.Equal(10, data["offset"]!.GetValue<int>());
    }

    [Fact]
    public async Task Get_envelope_exposes_the_memory_object()
    {
        JsonNode envelope = await RunJson(
            "{\"id\":\"m1\",\"memory\":\"x\",\"created_at\":\"2026-06-01T10:00:00Z\"}",
            (c, o, ct) => GetCommand.RunAsync(c, o, "m1", ct));

        AssertOkEnvelope(envelope);
        Assert.Equal("m1", envelope["data"]!["id"]!.GetValue<string>());
        Assert.Equal("x", envelope["data"]!["memory"]!.GetValue<string>());
    }
}
