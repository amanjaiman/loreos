namespace Lore.Agent.Capture;

/// <summary>Runs an ordered list of extractors and returns the first non-empty result —
/// the fallback policy at the heart of T002: UI Automation first (the clean path), OCR
/// only when UIA exposes no text. Tagging is preserved from whichever extractor produced
/// the text, so the source travels downstream. This orchestration is pure and fully
/// unit-tested; the platform extractors it composes are the untested seam.</summary>
public sealed class CompositeTextExtractor : ITextExtractor
{
    private readonly IReadOnlyList<ITextExtractor> _extractors;

    /// <param name="extractors">Tried in order; the first to return non-empty text wins.</param>
    public CompositeTextExtractor(params ITextExtractor[] extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);
        if (extractors.Length == 0)
        {
            throw new ArgumentException("At least one extractor is required.", nameof(extractors));
        }

        if (Array.IndexOf(extractors, null!) >= 0)
        {
            throw new ArgumentException("Extractors cannot contain a null entry.", nameof(extractors));
        }

        _extractors = extractors.ToArray();
    }

    public async Task<ExtractedText> ExtractAsync(
        WindowSnapshot window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsEmpty)
        {
            return ExtractedText.Empty;
        }

        foreach (ITextExtractor extractor in _extractors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractedText result = await extractor.ExtractAsync(window, cancellationToken).ConfigureAwait(false);
            if (!result.IsEmpty)
            {
                return result;
            }
        }

        return ExtractedText.Empty;
    }
}
