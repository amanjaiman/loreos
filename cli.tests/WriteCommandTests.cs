using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Lore.Cli.Commands;

namespace Lore.Cli.Tests;

/// <summary>The write commands (spec 007 T003): <c>add</c> posts an observation and reports the
/// memories memoryd distilled; <c>forget</c> orchestrates search-then-delete over the API and
/// removes the single best match. Tests drive the real <see cref="ApiClient"/> over a stubbed API.</summary>
public sealed class WriteCommandTests
{
    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task Add_reports_the_remembered_memories_in_human_mode()
    {
        using var handler = new StubHttpMessageHandler(_ => Json(
            HttpStatusCode.Created,
            "{\"results\":[{\"id\":\"m1\",\"memory\":\"The user prefers TypeScript\",\"event\":\"ADD\"}]}"));
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        int code = await AddCommand.RunAsync(client, output, "I like TypeScript", "coding", CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Remembered:", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("The user prefers TypeScript (added)", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_sends_the_text_and_category()
    {
        string? sentBody = null;
        using var handler = new StubHttpMessageHandler(req =>
        {
            // ReadAsStream() is synchronous (no blocking on a Task), so the analyzer is happy.
            using var reader = new StreamReader(req.Content!.ReadAsStream());
            sentBody = reader.ReadToEnd();
            return Json(HttpStatusCode.Created, "{\"results\":[]}");
        });
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var output = new Output(new StringWriter(), new StringWriter(), json: false);

        await AddCommand.RunAsync(client, output, "I like dark mode", "preferences", CancellationToken.None);

        Assert.NotNull(sentBody);
        Assert.Contains("\"text\":\"I like dark mode\"", sentBody, StringComparison.Ordinal);
        Assert.Contains("\"category\":\"preferences\"", sentBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_reports_a_no_op_when_nothing_new_was_distilled()
    {
        using var handler = new StubHttpMessageHandler(_ => Json(HttpStatusCode.Created, "{\"results\":[]}"));
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        int code = await AddCommand.RunAsync(client, output, "already known", "general", CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Contains("Nothing new to remember", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Add_json_forwards_the_results_envelope()
    {
        using var handler = new StubHttpMessageHandler(_ => Json(
            HttpStatusCode.Created, "{\"results\":[{\"id\":\"m1\",\"memory\":\"x\",\"event\":\"ADD\"}]}"));
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        await AddCommand.RunAsync(client, output, "x", "general", CancellationToken.None);

        JsonNode envelope = JsonNode.Parse(stdout.ToString())!;
        Assert.True(envelope["ok"]!.GetValue<bool>());
        Assert.Equal("ADD", envelope["data"]!["results"]!.AsArray()[0]!["event"]!.GetValue<string>());
    }

    // Routes the two calls forget makes: search → matches, delete → 204 (recording the deleted path).
    private static StubHttpMessageHandler ForgetHandler(string searchBody, Action<string>? onDelete = null) =>
        new(req =>
        {
            string path = req.RequestUri!.AbsolutePath;
            if (req.Method == HttpMethod.Post && path == "/memories/search")
            {
                return Json(HttpStatusCode.OK, searchBody);
            }

            if (req.Method == HttpMethod.Delete && path.StartsWith("/memories/", StringComparison.Ordinal))
            {
                onDelete?.Invoke(path);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

    [Fact]
    public async Task Forget_deletes_the_best_match_and_reports_the_rest()
    {
        string? deletedPath = null;
        using var handler = ForgetHandler(
            "{\"results\":[{\"id\":\"m1\",\"memory\":\"Uses Postgres\"},{\"id\":\"m2\",\"memory\":\"db stuff\"}]}",
            path => deletedPath = path);
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: false);

        int code = await ForgetCommand.RunAsync(client, output, "database", CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.Equal("/memories/m1", deletedPath);
        Assert.Contains("Forgot: Uses Postgres", stdout.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 other memory still match", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forget_json_reports_what_was_forgotten()
    {
        using var handler = ForgetHandler("{\"results\":[{\"id\":\"m1\",\"memory\":\"Uses Postgres\"}]}");
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        await ForgetCommand.RunAsync(client, output, "database", CancellationToken.None);

        JsonNode data = JsonNode.Parse(stdout.ToString())!["data"]!;
        Assert.True(data["forgotten"]!.GetValue<bool>());
        Assert.Equal("m1", data["id"]!.GetValue<string>());
        Assert.Equal(0, data["remaining_matches"]!.GetValue<int>());
    }

    [Fact]
    public async Task Forget_treats_no_match_as_a_successful_no_op()
    {
        using var handler = ForgetHandler("{\"results\":[]}");
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);

        int code = await ForgetCommand.RunAsync(client, output, "nonexistent", CancellationToken.None);

        Assert.Equal(ExitCodes.Success, code);
        Assert.False(JsonNode.Parse(stdout.ToString())!["data"]!["forgotten"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Forget_surfaces_a_delete_that_404s()
    {
        // Search finds a match, but the delete 404s (it vanished in between).
        using var handler = new StubHttpMessageHandler(req =>
            req.Method == HttpMethod.Post
                ? Json(HttpStatusCode.OK, "{\"results\":[{\"id\":\"m1\",\"memory\":\"x\"}]}")
                : Json(HttpStatusCode.NotFound, "{\"error\":\"no memory with id 'm1'\"}"));
        using var http = new HttpClient(handler);
        using var client = new ApiClient(httpClient: http);
        var stderr = new StringWriter();
        var output = new Output(new StringWriter(), stderr, json: false);

        int code = await ForgetCommand.RunAsync(client, output, "database", CancellationToken.None);

        Assert.Equal(ExitCodes.NotFound, code);
    }
}
