using System.Text.RegularExpressions;

namespace Lore.Agent.Capture;

/// <summary>The regex layer of the sensitivity chain: detects U.S. Social Security
/// numbers and payment-card numbers in text. Card candidates are confirmed with the Luhn
/// checksum so a random 16-digit run isn't mistaken for a card. Pure and static, so the
/// detector is exhaustively unit-testable — the trust-critical bar (constitution §6).
///
/// <para>Detection is intentionally format-anchored (separators required for SSNs,
/// word-boundary delimited for cards) to keep false positives low; when in doubt the
/// surrounding chain still fails closed at higher layers.</para></summary>
public static partial class SensitivePatterns
{
    // NNN-NN-NNNN or NNN NN NNNN (separators required — bare 9-digit runs are too common
    // to treat as SSNs without flooding false positives).
    [GeneratedRegex(@"\b\d{3}[ -]\d{2}[ -]\d{4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SsnRegex();

    // A run of 13–19 digits, optionally separated by single spaces or dashes — the shape
    // of a payment-card number before Luhn confirmation.
    [GeneratedRegex(@"\b\d(?:[ -]?\d){12,18}\b", RegexOptions.CultureInvariant)]
    private static partial Regex CardCandidateRegex();

    /// <summary><c>true</c> if <paramref name="text"/> contains an SSN or a Luhn-valid
    /// payment-card number.</summary>
    public static bool ContainsSensitive(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (SsnRegex().IsMatch(text))
        {
            return true;
        }

        foreach (Match match in CardCandidateRegex().Matches(text))
        {
            if (IsCardNumber(match.Value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCardNumber(string candidate)
    {
        // The candidate regex already guarantees 13–19 digits separated only by single
        // spaces or dashes, so the only thing left to confirm is the Luhn checksum — no
        // redundant length re-check (which would be an unreachable branch).
        Span<char> digits = stackalloc char[candidate.Length];
        int length = 0;
        foreach (char c in candidate)
        {
            if (char.IsAsciiDigit(c))
            {
                digits[length++] = c;
            }
        }

        return PassesLuhn(digits[..length]);
    }

    private static bool PassesLuhn(ReadOnlySpan<char> digits)
    {
        int sum = 0;
        bool doubleDigit = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int value = digits[i] - '0';
            if (doubleDigit)
            {
                value *= 2;
                if (value > 9)
                {
                    value -= 9;
                }
            }

            sum += value;
            doubleDigit = !doubleDigit;
        }

        return sum % 10 == 0;
    }
}
