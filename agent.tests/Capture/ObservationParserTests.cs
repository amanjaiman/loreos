using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class ObservationParserTests
{
    [Fact]
    public void Parses_clean_json()
    {
        CaptureAnalysis? result = ObservationParser.Parse(
            """{"observation": "I'm researching mechanical keyboards", "category": "shopping"}""");

        Assert.NotNull(result);
        Assert.Equal("I'm researching mechanical keyboards", result!.Observation);
        Assert.Equal("shopping", result.Category);
    }

    [Fact]
    public void Tolerates_markdown_code_fences()
    {
        const string raw = """
            ```json
            {"observation": "I'm reading about the French Revolution", "category": "reading"}
            ```
            """;

        CaptureAnalysis? result = ObservationParser.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal("I'm reading about the French Revolution", result!.Observation);
    }

    [Fact]
    public void Tolerates_surrounding_prose()
    {
        const string raw =
            "Sure! Here is the observation:\n{\"observation\": \"I'm planning a trip to Lisbon\", " +
            "\"category\": \"travel\"}\nHope that helps.";

        CaptureAnalysis? result = ObservationParser.Parse(raw);

        Assert.NotNull(result);
        Assert.Equal("I'm planning a trip to Lisbon", result!.Observation);
        Assert.Equal("travel", result.Category);
    }

    [Fact]
    public void Tolerates_a_trailing_comma()
    {
        CaptureAnalysis? result = ObservationParser.Parse(
            """{"observation": "I'm comparing laptops", "category": "shopping",}""");

        Assert.NotNull(result);
        Assert.Equal("I'm comparing laptops", result!.Observation);
    }

    [Fact]
    public void Trims_whitespace_around_values()
    {
        CaptureAnalysis? result = ObservationParser.Parse(
            """{"observation": "  I'm writing tests  ", "category": "  coding  "}""");

        Assert.NotNull(result);
        Assert.Equal("I'm writing tests", result!.Observation);
        Assert.Equal("coding", result.Category);
    }

    [Fact]
    public void Defaults_the_category_when_missing()
    {
        CaptureAnalysis? result = ObservationParser.Parse("""{"observation": "I'm doing something"}""");

        Assert.NotNull(result);
        Assert.Equal("general", result!.Category);
    }

    [Fact]
    public void Defaults_the_category_when_blank()
    {
        CaptureAnalysis? result = ObservationParser.Parse(
            """{"observation": "I'm doing something", "category": "   "}""");

        Assert.NotNull(result);
        Assert.Equal("general", result!.Category);
    }

    [Theory]
    [InlineData("""{"observation": "", "category": "reading"}""")]      // model's "nothing to remember"
    [InlineData("""{"observation": "   ", "category": "reading"}""")]   // whitespace only
    [InlineData("""{"category": "reading"}""")]                          // observation missing
    [InlineData("""{"observation": 42, "category": "reading"}""")]      // wrong type
    [InlineData("not json at all")]                                       // no object
    [InlineData("[1, 2, 3]")]                                             // array, not object — no braces matched
    [InlineData("{ this is not valid json }")]                           // braces but unparseable
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Returns_null_for_unusable_output(string? raw)
    {
        Assert.Null(ObservationParser.Parse(raw));
    }

    [Fact]
    public void A_json_array_root_is_rejected_even_though_it_has_brackets()
    {
        // Extraction keys on braces, so a top-level array yields no object and parses to null.
        Assert.Null(ObservationParser.Parse("""["observation", "category"]"""));
    }
}
