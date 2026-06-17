using System.Text.Json.Nodes;

namespace Lore.Cli.Tests;

/// <summary>The output layer renders exactly one of two modes (spec 007 T001): human text, or the
/// versioned <c>--json</c> envelope that is the machine contract (acceptance criterion 2). Both
/// modes scrub secrets before printing.</summary>
public sealed class OutputTests
{
    private static (Output Output, StringWriter Out, StringWriter Err) Make(bool json)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        return (new Output(stdout, stderr, json), stdout, stderr);
    }

    [Fact]
    public void Json_success_wraps_data_in_a_versioned_ok_envelope()
    {
        (Output output, StringWriter stdout, StringWriter stderr) = Make(json: true);

        output.WriteData(new Dictionary<string, object> { ["greeting"] = "hi" });

        JsonNode envelope = JsonNode.Parse(stdout.ToString())!;
        Assert.Equal("1.0", envelope["schema_version"]!.GetValue<string>());
        Assert.True(envelope["ok"]!.GetValue<bool>());
        Assert.Equal("hi", envelope["data"]!["greeting"]!.GetValue<string>());
        Assert.Null(envelope["error"]); // success omits error
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Json_error_fills_a_stable_code_and_message()
    {
        (Output output, StringWriter stdout, _) = Make(json: true);

        output.WriteError("not_found", "no memory with id 'x'");

        JsonNode envelope = JsonNode.Parse(stdout.ToString())!;
        Assert.False(envelope["ok"]!.GetValue<bool>());
        Assert.Equal("not_found", envelope["error"]!["code"]!.GetValue<string>());
        Assert.Equal("no memory with id 'x'", envelope["error"]!["message"]!.GetValue<string>());
        Assert.Null(envelope["data"]); // failure omits data
    }

    [Fact]
    public void Human_mode_writes_text_to_stdout_and_nothing_to_stderr()
    {
        (Output output, StringWriter stdout, StringWriter stderr) = Make(json: false);

        output.WriteLine("a recent memory");

        Assert.Contains("a recent memory", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    [Fact]
    public void Human_mode_errors_go_to_stderr_not_stdout()
    {
        (Output output, StringWriter stdout, StringWriter stderr) = Make(json: false);

        output.WriteError("agent_unreachable", "Lore isn't running.");

        Assert.Equal(string.Empty, stdout.ToString());
        Assert.Contains("Lore isn't running.", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Json_data_is_scrubbed_of_secret_material()
    {
        (Output output, StringWriter stdout, _) = Make(json: true);

        output.WriteData(new Dictionary<string, object?>
        {
            ["api_key"] = "sk-secret-value",
            ["api_key_ref"] = "lore/provider/openai", // a handle, not a secret — kept
            ["model"] = "gpt-4o-mini",
        });

        JsonNode data = JsonNode.Parse(stdout.ToString())!["data"]!;
        Assert.Equal(SecretScrubber.Placeholder, data["api_key"]!.GetValue<string>());
        Assert.Equal("lore/provider/openai", data["api_key_ref"]!.GetValue<string>());
        Assert.Equal("gpt-4o-mini", data["model"]!.GetValue<string>());
        Assert.DoesNotContain("sk-secret-value", stdout.ToString(), StringComparison.Ordinal);
    }
}
