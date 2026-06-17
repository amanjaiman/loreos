using System.CommandLine;

namespace Lore.Cli.Tests;

/// <summary>The root command wiring (spec 007 T001): the two global options parse as expected, and
/// the shared <c>--api-url</c> resolver accepts valid loopback/remote URLs while rejecting garbage
/// as a bad-usage error (the foundation the exit-code scheme builds on in T005).</summary>
public sealed class CliRootTests
{
    [Fact]
    public void Parses_the_global_json_flag()
    {
        ParseResult result = CliRoot.Build().Parse(new[] { "--json" });

        Assert.Empty(result.Errors);
        Assert.True(result.GetValue(CliRoot.JsonOption));
    }

    [Fact]
    public void Defaults_api_url_to_loopback()
    {
        ParseResult result = CliRoot.Build().Parse(Array.Empty<string>());

        Assert.Equal(ApiClient.DefaultBaseUrl.ToString(), result.GetValue(CliRoot.ApiUrlOption));
    }

    [Fact]
    public void Reports_an_unrecognized_command()
    {
        ParseResult result = CliRoot.Build().Parse(new[] { "definitely-not-a-command" });

        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void ResolveApiUrl_uses_the_loopback_default_when_absent()
    {
        ParseResult result = CliRoot.Build().Parse(Array.Empty<string>());
        Output output = HumanOutput();

        bool ok = CliRoot.TryResolveApiUrl(result, output, out Uri apiUrl);

        Assert.True(ok);
        Assert.Equal(ApiClient.DefaultBaseUrl, apiUrl);
    }

    [Fact]
    public void ResolveApiUrl_accepts_a_valid_override()
    {
        ParseResult result = CliRoot.Build().Parse(new[] { "--api-url", "http://127.0.0.1:9000" });
        Output output = HumanOutput();

        bool ok = CliRoot.TryResolveApiUrl(result, output, out Uri apiUrl);

        Assert.True(ok);
        Assert.Equal(new Uri("http://127.0.0.1:9000"), apiUrl);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://127.0.0.1:7842")]
    public void ResolveApiUrl_rejects_a_non_http_url_as_bad_usage(string bad)
    {
        ParseResult result = CliRoot.Build().Parse(new[] { "--api-url", bad });
        var stderr = new StringWriter();
        var output = new Output(new StringWriter(), stderr, json: false);

        bool ok = CliRoot.TryResolveApiUrl(result, output, out _);

        Assert.False(ok);
        Assert.Contains("--api-url", stderr.ToString(), StringComparison.Ordinal);
    }

    private static Output HumanOutput() => new(new StringWriter(), new StringWriter(), json: false);
}
