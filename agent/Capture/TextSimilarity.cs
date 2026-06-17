namespace Lore.Agent.Capture;

/// <summary>The diff math the smart gate (T005) uses to decide whether a window's content
/// has meaningfully changed since the last capture. <see cref="Similarity"/> is a
/// token-set Jaccard ratio in [0, 1] — 1 means the same set of words, 0 means no overlap.
/// Set-based (not order- or frequency-based) on purpose: scrolling a page or reflowing
/// text shuffles word positions but keeps the vocabulary, and that should read as
/// "unchanged" so the gate skips a redundant inference call. Pure and unit-tested.</summary>
public static class TextSimilarity
{
    /// <summary>Token-set Jaccard similarity of two texts, in [0, 1]. Two empty texts are
    /// identical (1.0 — no change); one empty and one not share nothing (0.0).</summary>
    public static double Similarity(string? a, string? b)
    {
        HashSet<string> tokensA = Tokenize(a);
        HashSet<string> tokensB = Tokenize(b);

        if (tokensA.Count == 0 && tokensB.Count == 0)
        {
            return 1.0;
        }

        if (tokensA.Count == 0 || tokensB.Count == 0)
        {
            return 0.0;
        }

        int intersection = 0;
        foreach (string token in tokensA)
        {
            if (tokensB.Contains(token))
            {
                intersection++;
            }
        }

        int union = tokensA.Count + tokensB.Count - intersection;
        return (double)intersection / union;
    }

    /// <summary>How much two texts differ, in [0, 1] — the complement of
    /// <see cref="Similarity"/>. Convenience for gates phrased in terms of a diff
    /// threshold.</summary>
    public static double Difference(string? a, string? b) => 1.0 - Similarity(a, b);

    private static HashSet<string> Tokenize(string? text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(text))
        {
            return tokens;
        }

        int start = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                {
                    start = i;
                }
            }
            else if (start >= 0)
            {
                tokens.Add(text[start..i].ToUpperInvariant());
                start = -1;
            }
        }

        if (start >= 0)
        {
            tokens.Add(text[start..].ToUpperInvariant());
        }

        return tokens;
    }
}
