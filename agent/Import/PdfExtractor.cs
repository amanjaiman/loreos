using System.IO;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Lore.Agent.Import;

/// <summary>The seam the import service (009 T003) extracts document text through, so it can be
/// faked in tests without crafting real PDFs and so a future plain-text/markdown reader can sit
/// behind the same call.</summary>
public interface IPdfExtractor
{
    /// <inheritdoc cref="PdfExtractor.Extract"/>
    PdfExtractionResult Extract(Stream pdf);
}

/// <summary>Turns a PDF into plain text for the import pipeline (spec 009 T001). Pure
/// extraction — no filtering, chunking, or storage; those are later stages. PdfPig reads the
/// document content streams directly (no OCR), so a born-digital PDF extracts faithfully while a
/// scanned/image-only PDF yields little or no text. That low-text case is surfaced as a warning
/// (<see cref="PdfExtractionResult.LowText"/>) rather than a silent empty import, so the caller
/// can tell the user "this looks scanned" instead of importing nothing (spec risk: extraction
/// quality varies).</summary>
public sealed class PdfExtractor : IPdfExtractor
{
    /// <summary>Below this many extractable non-whitespace characters per page the document is
    /// treated as low-text (likely scanned/image-only). Generous enough that a sparse cover page
    /// among real text pages does not trip the warning, low enough that a wholly image-based PDF
    /// always does.</summary>
    internal const int LowTextCharsPerPage = 8;

    /// <summary>Extract <paramref name="pdf"/> to text, page order preserved, pages separated by a
    /// blank line so the chunker sees paragraph boundaries. The stream is read in full; the caller
    /// owns its lifetime.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="pdf"/> is null.</exception>
    public PdfExtractionResult Extract(Stream pdf)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var text = new StringBuilder();
        int pageCount = 0;
        long nonWhitespace = 0;

        using (PdfDocument document = PdfDocument.Open(pdf))
        {
            foreach (Page page in document.GetPages())
            {
                // ContentOrderTextExtractor reconstructs reading order (and word spacing) from the
                // glyph positions — page.Text alone concatenates letters without spaces.
                string pageText = ContentOrderTextExtractor.GetText(page) ?? string.Empty;
                if (pageCount > 0)
                {
                    text.Append("\n\n");
                }

                text.Append(pageText);
                pageCount++;
                nonWhitespace += CountNonWhitespace(pageText);
            }
        }

        // A page-less document can't be "low text" (there's nothing to have expected); only flag
        // documents that have pages but almost no extractable text on them.
        bool lowText = pageCount > 0 && nonWhitespace < (long)pageCount * LowTextCharsPerPage;
        return new PdfExtractionResult(text.ToString(), pageCount, lowText);
    }

    private static long CountNonWhitespace(string text)
    {
        long count = 0;
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                count++;
            }
        }

        return count;
    }
}

/// <summary>The result of extracting a PDF: the recovered <see cref="Text"/>, how many
/// <see cref="PageCount">pages</see> it had, and whether it looks <see cref="LowText">low-text</see>
/// (scanned/image-only) so the caller can warn rather than import an empty document.</summary>
public sealed record PdfExtractionResult(string Text, int PageCount, bool LowText);
