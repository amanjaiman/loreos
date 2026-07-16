using Lore.Agent.Distill;

namespace Lore.Agent.Tests.Distill;

public sealed class DistillParserTests
{
    [Fact]
    public void Parses_a_clean_fact()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """
            {"facts": [{"statement": "I'm recovering from a wisdom tooth extraction.",
                        "kind": "state", "confidence": 0.7, "horizon_days": 30}]}
            """);

        CandidateFact fact = Assert.Single(facts!);
        Assert.Equal("I'm recovering from a wisdom tooth extraction.", fact.Statement);
        Assert.Equal("state", fact.Kind);
        Assert.Equal(0.7, fact.Confidence);
        Assert.Equal(30, fact.HorizonDays);
    }

    [Fact]
    public void Empty_facts_is_the_normal_answer_not_a_failure()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse("""{"facts": []}""");

        Assert.NotNull(facts);
        Assert.Empty(facts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here at all")]
    [InlineData("{\"facts\": \"not an array\"}")]
    [InlineData("{\"other\": []}")]
    [InlineData("{broken json")]
    public void Unusable_output_returns_null(string? raw)
    {
        Assert.Null(DistillParser.Parse(raw));
    }

    [Fact]
    public void Tolerates_markdown_fences_and_preamble()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """
            Sure! Here is the JSON:
            ```json
            {"facts": [{"statement": "I booked a trip to Paris for May.", "kind": "experience", "confidence": 0.9}]}
            ```
            """);

        Assert.Equal("experience", Assert.Single(facts!).Kind);
    }

    [Fact]
    public void Caps_at_three_facts_keeping_the_strongest()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """
            {"facts": [
              {"statement": "A", "kind": "preference", "confidence": 0.2},
              {"statement": "B", "kind": "preference", "confidence": 0.9},
              {"statement": "C", "kind": "preference", "confidence": 0.5},
              {"statement": "D", "kind": "preference", "confidence": 0.7},
              {"statement": "E", "kind": "preference", "confidence": 0.6}
            ]}
            """);

        Assert.Equal(3, facts!.Count);
        Assert.Equal(["B", "D", "E"], facts.Select(f => f.Statement).ToArray());
    }

    [Fact]
    public void Drops_unknown_kinds_blank_and_oversized_statements()
    {
        string oversized = new('x', DistillParser.MaxStatementChars + 1);
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            $$"""
            {"facts": [
              {"statement": "I collect vinyl records.", "kind": "vibe", "confidence": 0.9},
              {"statement": "", "kind": "preference", "confidence": 0.9},
              {"statement": "{{oversized}}", "kind": "preference", "confidence": 0.9},
              {"statement": "I collect vinyl records.", "kind": "preference", "confidence": 0.6},
              "not even an object"
            ]}
            """);

        CandidateFact fact = Assert.Single(facts!);
        Assert.Equal("preference", fact.Kind);
    }

    [Fact]
    public void Confidence_is_clamped_and_defaults_when_missing()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """
            {"facts": [
              {"statement": "A", "kind": "identity", "confidence": 7.5},
              {"statement": "B", "kind": "identity"}
            ]}
            """);

        Assert.Equal(1.0, facts![0].Confidence);
        Assert.Equal(0.5, facts[1].Confidence);
    }

    [Fact]
    public void Horizon_days_only_applies_to_state_facts()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """
            {"facts": [
              {"statement": "I visited France.", "kind": "experience", "confidence": 0.9, "horizon_days": 10},
              {"statement": "I'm on crutches.", "kind": "state", "confidence": 0.7, "horizon_days": 21}
            ]}
            """);

        Assert.Null(facts![0].HorizonDays); // ignored for non-state kinds
        Assert.Equal(21, facts[1].HorizonDays);
    }

    [Fact]
    public void Horizon_days_ignores_non_positive_and_caps_oversized_values()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            $$"""
            {"facts": [
              {"statement": "A", "kind": "state", "confidence": 0.9, "horizon_days": 0},
              {"statement": "B", "kind": "state", "confidence": 0.8, "horizon_days": -14},
              {"statement": "C", "kind": "state", "confidence": 0.7, "horizon_days": {{DistillParser.MaxHorizonDays + 1}}}
            ]}
            """);

        Assert.Null(facts![0].HorizonDays);
        Assert.Null(facts[1].HorizonDays);
        Assert.Equal(DistillParser.MaxHorizonDays, facts[2].HorizonDays);
    }

    [Fact]
    public void Kind_matching_is_case_insensitive()
    {
        IReadOnlyList<CandidateFact>? facts = DistillParser.Parse(
            """{"facts": [{"statement": "I live in Boston.", "kind": "Identity", "confidence": 0.8}]}""");

        Assert.Equal("identity", Assert.Single(facts!).Kind);
    }
}
