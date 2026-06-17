using Lore.Agent.Capture;
using Lore.Agent.Inference;

namespace Lore.Agent.Tests.Capture;

public sealed class AnalysisPromptTests
{
    [Fact]
    public void Includes_the_title_and_text_in_the_user_prompt()
    {
        InferenceRequest request = AnalysisPrompt.Build("My Title", "some body text");

        Assert.Contains("My Title", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("some body text", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void The_system_prompt_asks_for_a_first_person_json_observation()
    {
        InferenceRequest request = AnalysisPrompt.Build("t", "x");

        Assert.Contains("first-person", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("observation", request.SystemPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_title_falls_back_to_a_placeholder(string? title)
    {
        InferenceRequest request = AnalysisPrompt.Build(title, "body");

        Assert.Contains("(untitled)", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_text_is_truncated_to_bound_token_cost()
    {
        string huge = new('a', 10_000);

        InferenceRequest request = AnalysisPrompt.Build("title", huge);

        // The prompt has a header plus at most the 4000-char cap of body text.
        Assert.DoesNotContain(new string('a', 4001), request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains(new string('a', 4000), request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Short_text_is_passed_through_untruncated()
    {
        InferenceRequest request = AnalysisPrompt.Build("title", "brief note");

        Assert.Contains("brief note", request.UserPrompt, StringComparison.Ordinal);
    }
}
