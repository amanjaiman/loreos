using Lore.Agent.Capture;

namespace Lore.Agent.Tests.Capture;

public sealed class SensitivePatternsTests
{
    [Theory]
    // SSNs — separators required.
    [InlineData("my ssn is 123-45-6789 ok", true)]
    [InlineData("spaced 123 45 6789 form", true)]
    [InlineData("mixed 123-45 6789 form", true)]
    [InlineData("bare 123456789 is not flagged", false)]
    // Payment cards — Luhn-confirmed.
    [InlineData("visa 4111111111111111 here", true)]            // 16-digit, valid
    [InlineData("spaced 4111 1111 1111 1111 card", true)]       // separators stripped
    [InlineData("dashed 4111-1111-1111-1111 card", true)]
    [InlineData("amex 378282246310005 ok", true)]                // 15-digit, valid (hits Luhn >9)
    [InlineData("mc 5555555555554444 ok", true)]                 // 16-digit, valid (hits Luhn >9)
    [InlineData("short visa 4222222222222 ok", true)]            // 13-digit boundary, valid
    [InlineData("typo 4111111111111112 not a card", false)]     // fails Luhn
    [InlineData("only 411111111111 digits", false)]              // 12 digits — below card length
    [InlineData("phone 123 456 7890 here", false)]               // 10 digits — neither SSN nor card
    [InlineData("plain prose with no numbers", false)]
    public void Detects_sensitive_patterns(string text, bool expected)
    {
        Assert.Equal(expected, SensitivePatterns.ContainsSensitive(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_is_never_sensitive(string? text)
    {
        Assert.False(SensitivePatterns.ContainsSensitive(text));
    }

    [Fact]
    public void Finds_a_card_among_surrounding_text_and_other_numbers()
    {
        const string text = "order 12 of 99, pay with 4111 1111 1111 1111 by friday";
        Assert.True(SensitivePatterns.ContainsSensitive(text));
    }
}
