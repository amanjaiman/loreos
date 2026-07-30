namespace Lore.Agent.Capture;

/// <summary>Runs an ordered list of extractors and returns the first result that carries
/// real content: UI Automation first (the clean path), OCR when UIA exposes no <em>usable</em>
/// text. Tagging is preserved from whichever extractor produced the text, so the source
/// travels downstream. This orchestration is pure and fully unit-tested; the platform
/// extractors it composes are the untested seam.
///
/// <para>"Usable" matters: on a GPU-composited or fullscreen window (a game, a WebGL/canvas
/// app, a remote-desktop session) UIA's tree often exposes nothing but the root window's
/// <em>name</em> — i.e. the window title we already have. Treating that title-only result as
/// success would short-circuit the richer OCR path and leave the pixels unread. So a result
/// that is nothing beyond the title is kept only as a floor while the next extractor is
/// tried; OCR's real content wins when it reads any, and the title floor is returned only if
/// nothing better turns up (never a regression to empty).</para></summary>
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

        // A non-empty result that is nothing but the window title — kept as a floor while we
        // try later extractors for the actual content (see the class summary).
        ExtractedText titleFloor = ExtractedText.Empty;

        foreach (ITextExtractor extractor in _extractors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExtractedText result = await extractor.ExtractAsync(window, cancellationToken).ConfigureAwait(false);
            if (result.IsEmpty)
            {
                continue;
            }

            if (CarriesContent(result.Text, window.Title))
            {
                return result; // real content — the fast path (UIA on ordinary apps stops here)
            }

            if (titleFloor.IsEmpty)
            {
                titleFloor = result; // remember the title-only result and keep trying OCR
            }
        }

        return titleFloor;
    }

    // A result carries content when, ignoring surrounding whitespace, it is more than just the
    // window title we already know — which is exactly what lets OCR run for a fullscreen game
    // whose UIA tree exposes only the window's name.
    private static bool CarriesContent(string text, string title)
    {
        string trimmed = text.Trim();
        return trimmed.Length > 0
            && !string.Equals(trimmed, title.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
