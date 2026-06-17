using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Lore.Cli.Commands;

namespace Lore.Cli.Tests;

/// <summary>The admin commands (spec 007 T004): <c>status</c> reports component health, <c>config
/// get/set</c> reads and changes config without ever echoing a secret, and <c>export</c> emits the
/// raw export document for redirection.</summary>
public sealed class AdminCommandTests
{
    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static (ApiClient Client, HttpClient Http, StubHttpMessageHandler Handler) Stub(
        Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var handler = new StubHttpMessageHandler(responder);
        var http = new HttpClient(handler);
        return (new ApiClient(httpClient: http), http, handler);
    }

    [Fact]
    public async Task Status_renders_component_health_in_human_mode()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok(
            "{\"version\":\"0.1.0\",\"api_version\":\"1.0\",\"components\":{\"agent\":\"running\",\"memoryd\":\"ready\",\"provider\":\"unconfigured\"}}"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await StatusCommand.RunAsync(client, output, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.Contains("memoryd:  ready", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("provider: unconfigured", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("version 0.1.0 (API 1.0)", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Status_json_forwards_the_status_object()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok(
            "{\"version\":\"0.1.0\",\"api_version\":\"1.0\",\"components\":{\"agent\":\"running\",\"memoryd\":\"ready\",\"provider\":\"ready\"}}"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: true);

            await StatusCommand.RunAsync(client, output, CancellationToken.None);

            JsonNode data = JsonNode.Parse(stdout.ToString())!["data"]!;
            Assert.Equal("running", data["components"]!["agent"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Config_get_whole_config_scrubs_inline_secrets()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok(
            "{\"provider\":{\"model\":\"gpt-4o\",\"api_key\":\"sk-should-not-appear\"}}"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await ConfigCommand.GetAsync(client, output, key: null, CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.DoesNotContain("sk-should-not-appear", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("gpt-4o", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Config_get_single_key_prints_the_value()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok("{\"provider\":{\"model\":\"gpt-4o-mini\"}}"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await ConfigCommand.GetAsync(client, output, "provider.model", CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.Contains("gpt-4o-mini", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Config_get_missing_key_is_not_found()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok("{\"provider\":{\"model\":\"gpt-4o\"}}"));
        using (http)
        using (client)
        {
            var stderr = new StringWriter();
            var output = new Output(new StringWriter(), stderr, json: false);

            int code = await ConfigCommand.GetAsync(client, output, "provider.missing", CancellationToken.None);

            Assert.Equal(ExitCodes.NotFound, code);
        }
    }

    [Fact]
    public async Task Config_set_sends_a_nested_patch_and_confirms()
    {
        string? sentBody = null;
        (ApiClient client, HttpClient http, _) = Stub(req =>
        {
            using var reader = new StreamReader(req.Content!.ReadAsStream());
            sentBody = reader.ReadToEnd();
            return Ok("{\"provider\":{\"model\":\"gpt-4o-mini\"}}");
        });
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await ConfigCommand.SetAsync(client, output, "provider.model", "gpt-4o-mini", CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.Contains("\"provider\":{\"model\":\"gpt-4o-mini\"}", sentBody!.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.Contains("Set provider.model = gpt-4o-mini", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Config_set_parses_a_scalar_value()
    {
        string? sentBody = null;
        (ApiClient client, HttpClient http, _) = Stub(req =>
        {
            using var reader = new StreamReader(req.Content!.ReadAsStream());
            sentBody = reader.ReadToEnd();
            return Ok("{}");
        });
        using (http)
        using (client)
        {
            var output = new Output(new StringWriter(), new StringWriter(), json: false);

            await ConfigCommand.SetAsync(client, output, "capture.enabled", "true", CancellationToken.None);

            // The boolean is sent unquoted, not as the string "true".
            Assert.Contains("\"enabled\":true", sentBody!.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Config_set_never_echoes_a_secret_value()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok("{\"provider\":{\"api_key_ref\":\"lore/provider/openai\"}}"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await ConfigCommand.SetAsync(client, output, "provider.api_key", "sk-super-secret", CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.DoesNotContain("sk-super-secret", stdout.ToString(), StringComparison.Ordinal);
            Assert.Contains("keystore", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Export_json_emits_the_raw_document_not_an_envelope()
    {
        const string ExportDoc = "{\"exported_at\":\"2026-06-01T00:00:00Z\",\"count\":1,\"memories\":[{\"id\":\"m1\",\"memory\":\"x\"}]}";
        (ApiClient client, HttpClient http, StubHttpMessageHandler handler) = Stub(_ => Ok(ExportDoc));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: true);

            int code = await ExportCommand.RunAsync(client, output, "json", CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.Contains("/export/json", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.Contains("exported_at", stdout.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("schema_version", stdout.ToString(), StringComparison.Ordinal); // raw, not enveloped
        }
    }

    [Fact]
    public async Task Export_markdown_hits_the_markdown_endpoint()
    {
        (ApiClient client, HttpClient http, StubHttpMessageHandler handler) = Stub(_ => Ok("# Lore memory export\n"));
        using (http)
        using (client)
        {
            var stdout = new StringWriter();
            var output = new Output(stdout, new StringWriter(), json: false);

            int code = await ExportCommand.RunAsync(client, output, "markdown", CancellationToken.None);

            Assert.Equal(ExitCodes.Success, code);
            Assert.Contains("/export/markdown", handler.LastRequest!.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.Contains("# Lore memory export", stdout.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Export_rejects_an_unknown_format()
    {
        (ApiClient client, HttpClient http, _) = Stub(_ => Ok("{}"));
        using (http)
        using (client)
        {
            var stderr = new StringWriter();
            var output = new Output(new StringWriter(), stderr, json: false);

            int code = await ExportCommand.RunAsync(client, output, "yaml", CancellationToken.None);

            Assert.Equal(ExitCodes.BadUsage, code);
            Assert.Contains("--format", stderr.ToString(), StringComparison.Ordinal);
        }
    }
}
