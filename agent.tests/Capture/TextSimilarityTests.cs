using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class TextSimilarityTests
{
    [Fact]
    public void Identical_text_is_fully_similar()
    {
        Assert.Equal(1.0, TextSimilarity.Similarity("the quick brown fox", "the quick brown fox"));
    }

    [Fact]
    public void Reordered_and_recased_words_are_still_identical_token_sets()
    {
        // Set-based: order and case don't matter, so a scrolled/reflowed page reads as unchanged.
        Assert.Equal(1.0, TextSimilarity.Similarity("Quick brown FOX", "fox quick brown"));
    }

    [Fact]
    public void Disjoint_text_has_zero_similarity()
    {
        Assert.Equal(0.0, TextSimilarity.Similarity("alpha beta gamma", "delta epsilon zeta"));
    }

    [Fact]
    public void Partial_overlap_falls_between_zero_and_one()
    {
        // {a,b,c,d} vs {c,d,e,f}: intersection 2, union 6 → 1/3.
        double similarity = TextSimilarity.Similarity("a b c d", "c d e f");
        Assert.Equal(1.0 / 3.0, similarity, precision: 10);
    }

    [Fact]
    public void Two_empty_texts_count_as_identical_no_change()
    {
        Assert.Equal(1.0, TextSimilarity.Similarity("", ""));
        Assert.Equal(1.0, TextSimilarity.Similarity(null, null));
    }

    [Theory]
    [InlineData("something", "")]
    [InlineData("", "something")]
    [InlineData("something", null)]
    public void Empty_versus_non_empty_shares_nothing(string? a, string? b)
    {
        Assert.Equal(0.0, TextSimilarity.Similarity(a, b));
    }

    [Fact]
    public void Punctuation_is_not_part_of_a_token()
    {
        Assert.Equal(1.0, TextSimilarity.Similarity("hello, world!", "hello world"));
    }

    [Fact]
    public void Similarity_is_symmetric()
    {
        double forward = TextSimilarity.Similarity("a b c d", "c d e f");
        double backward = TextSimilarity.Similarity("c d e f", "a b c d");
        Assert.Equal(forward, backward, precision: 12);
    }

    [Fact]
    public void Difference_is_the_complement_of_similarity()
    {
        Assert.Equal(0.0, TextSimilarity.Difference("same words", "same words"));
        Assert.Equal(1.0, TextSimilarity.Difference("alpha", "omega"));
        Assert.Equal(2.0 / 3.0, TextSimilarity.Difference("a b c d", "c d e f"), precision: 10);
    }
}
