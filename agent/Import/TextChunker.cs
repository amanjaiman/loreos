using System.Text.RegularExpressions;

namespace Lore.Agent.Import;

/// <summary>Splits document text into sized, overlapped chunks for mem0 extraction (spec 009 T001).
/// Two knobs: a target chunk size and an overlap. Chunks are cut on whitespace where possible so a
/// word or sentence is not sliced mid-token, and consecutive chunks share <c>overlap</c> trailing/
/// leading characters so a fact straddling a boundary still appears whole in one chunk. Pure and
/// deterministic — no I/O, no state — so it is exhaustively unit-testable (constitution §5).</summary>
public sealed partial class TextChunker
{
    private readonly int _chunkSize;
    private readonly int _overlap;

    /// <summary>Collapse any run of whitespace (including the page-break blank lines the extractor
    /// inserts) to a single space, so chunk sizing measures real content, not layout.</summary>
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <param name="chunkSize">Target maximum characters per chunk. Must be positive.</param>
    /// <param name="overlap">Characters each chunk shares with the previous one. Must be in
    /// <c>[0, chunkSize)</c> — an overlap at or above the size could never advance.</param>
    public TextChunker(int chunkSize = 1000, int overlap = 100)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "chunk size must be positive");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overlap), overlap, "overlap must be in [0, chunkSize)");
        }

        _chunkSize = chunkSize;
        _overlap = overlap;
    }

    /// <summary>Split <paramref name="text"/> into chunks. Whitespace is first normalized; an empty
    /// or whitespace-only document yields no chunks, and a document shorter than one chunk yields a
    /// single chunk.</summary>
    public IReadOnlyList<string> Split(string? text)
    {
        string normalized = Whitespace().Replace(text ?? string.Empty, " ").Trim();
        if (normalized.Length == 0)
        {
            return [];
        }

        if (normalized.Length <= _chunkSize)
        {
            return [normalized];
        }

        var chunks = new List<string>();
        int start = 0;
        while (start < normalized.Length)
        {
            int end = Math.Min(start + _chunkSize, normalized.Length);

            // Prefer to cut on whitespace so a word isn't split. Only back off within this chunk
            // (never before its midpoint) so a long unbroken run still makes progress via a hard cut.
            if (end < normalized.Length)
            {
                int breakAt = normalized.LastIndexOf(' ', end - 1, end - start);
                if (breakAt > start + (_chunkSize / 2))
                {
                    end = breakAt;
                }
            }

            chunks.Add(normalized[start..end].Trim());

            if (end >= normalized.Length)
            {
                break;
            }

            // Step forward, re-reading the last `overlap` characters into the next chunk. Guard
            // against a degenerate step (e.g. a tiny chunk after backoff) so we always advance.
            int next = end - _overlap;
            start = next > start ? next : end;
        }

        return chunks;
    }
}
