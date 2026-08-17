using Lore.Agent.Capture;
using Lore.Agent.Capture.Episodes;
using Lore.Agent.Distill;
using Lore.Agent.Inference;

namespace Lore.Agent.Tests.Distill;

public sealed class DistillPromptTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static Episode Episode(params ContentTypeTally[] mix) => new(
        "ep-1",
        T0,
        T0 + TimeSpan.FromMinutes(25),
        ["browser"],
        ["Espresso machines — buying guide"],
        ["add to cart 64mm burr grinder", "extraction ratios explained"],
        9,
        mix);

    // v2-008 R6.1. ContentClassifier already ran on every one of these observations; before
    // this the answer died at episode close and the model had to re-infer "shopping" from
    // the samples it was given.
    [Fact]
    public void The_content_type_mix_reaches_the_user_message_with_counts()
    {
        InferenceRequest request = DistillPrompt.Build(Episode(
            new ContentTypeTally(ContentType.Shopping, 7),
            new ContentTypeTally(ContentType.Reading, 2)));

        Assert.Contains("Content: Shopping (7), Reading (2)", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mix_of_only_unknown_renders_no_content_line_at_all()
    {
        // "Content: Unknown (9)" is prompt weight carrying no signal, and prompt bloat has a
        // cost on every episode of every day.
        InferenceRequest request = DistillPrompt.Build(
            Episode(new ContentTypeTally(ContentType.Unknown, 9)));

        Assert.DoesNotContain("Content:", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_is_kept_when_it_sits_beside_a_real_kind()
    {
        // Here it is load-bearing: it says two-thirds of the episode was NOT shopping.
        InferenceRequest request = DistillPrompt.Build(Episode(
            new ContentTypeTally(ContentType.Unknown, 6),
            new ContentTypeTally(ContentType.Shopping, 3)));

        Assert.Contains("Content: Unknown (6), Shopping (3)", request.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void An_episode_read_from_storage_still_builds_a_prompt()
    {
        // The mix is not persisted, so an episode reconstituted from the episodes table
        // carries an empty one. That must degrade to the pre-R6.1 prompt, not throw.
        InferenceRequest request = DistillPrompt.Build(Episode());

        Assert.DoesNotContain("Content:", request.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Observations: 9", request.UserPrompt, StringComparison.Ordinal);
        Assert.Equal(0.0, request.Temperature);
    }
}
