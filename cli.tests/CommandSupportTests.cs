using System.Net;
using System.Text.Json.Nodes;
using Lore.Cli.Commands;

namespace Lore.Cli.Tests;

/// <summary>The shared failure mapping (spec 007 T002, completed by T005): a non-success API status
/// becomes the documented exit code and a stable error code, preferring the API's own error message.</summary>
public sealed class CommandSupportTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound, ExitCodes.NotFound, "not_found")]
    [InlineData(HttpStatusCode.BadRequest, ExitCodes.BadUsage, "bad_usage")]
    [InlineData(HttpStatusCode.InternalServerError, ExitCodes.RuntimeFailure, "runtime_error")]
    public void Fail_maps_status_to_exit_code_and_error_code(HttpStatusCode status, int expectedExit, string expectedCode)
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);
        var response = new ApiResponse(status, "{\"error\":\"boom\"}");

        int exit = CommandSupport.Fail(output, response);

        Assert.Equal(expectedExit, exit);
        JsonNode error = JsonNode.Parse(stdout.ToString())!["error"]!;
        Assert.Equal(expectedCode, error["code"]!.GetValue<string>());
        Assert.Equal("boom", error["message"]!.GetValue<string>());
    }

    [Fact]
    public void Fail_falls_back_to_a_status_message_when_the_body_has_no_error()
    {
        var stdout = new StringWriter();
        var output = new Output(stdout, new StringWriter(), json: true);
        var response = new ApiResponse(HttpStatusCode.BadGateway, "not json at all");

        int exit = CommandSupport.Fail(output, response);

        Assert.Equal(ExitCodes.RuntimeFailure, exit);
        Assert.Contains("HTTP 502", JsonNode.Parse(stdout.ToString())!["error"]!["message"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void AgentDownMessage_names_the_target_and_is_actionable()
    {
        string message = CommandSupport.AgentDownMessage(new Uri("http://127.0.0.1:7842"));

        Assert.Contains("isn't running", message, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:7842", message, StringComparison.Ordinal);
    }
}
