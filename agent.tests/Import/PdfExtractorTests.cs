using System.IO;
using Lore.Agent.Import;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Lore.Agent.Tests.Import;

/// <summary>Spec 009 T001: the PDF extractor recovers text from a born-digital PDF and flags a
/// text-less (scanned/image-only) PDF as low-text rather than importing nothing. PDFs are built
/// in-memory with PdfPig's writer so the cases are self-contained — no fixture files.</summary>
public sealed class PdfExtractorTests
{
    [Fact]
    public void Extracts_text_from_every_page_in_order()
    {
        byte[] pdf = BuildPdf("I live in Seattle.", "My dog is named Pixel.");

        PdfExtractionResult result = new PdfExtractor().Extract(new MemoryStream(pdf));

        Assert.Equal(2, result.PageCount);
        Assert.False(result.LowText);
        Assert.Contains("Seattle", result.Text, StringComparison.Ordinal);
        Assert.Contains("Pixel", result.Text, StringComparison.Ordinal);
        // Page order is preserved.
        Assert.True(
            result.Text.IndexOf("Seattle", StringComparison.Ordinal)
                < result.Text.IndexOf("Pixel", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_less_pdf_is_flagged_low_text()
    {
        // A page with no text layer stands in for a scanned/image-only PDF.
        byte[] pdf = BuildPdf((string?)null);

        PdfExtractionResult result = new PdfExtractor().Extract(new MemoryStream(pdf));

        Assert.Equal(1, result.PageCount);
        Assert.True(result.LowText);
    }

    [Fact]
    public void Extract_rejects_a_null_stream()
    {
        Assert.Throws<ArgumentNullException>(() => new PdfExtractor().Extract(null!));
    }

    /// <summary>Build a single- or multi-page PDF, one string per page (null = an empty page).</summary>
    private static byte[] BuildPdf(params string?[] pages)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (string? text in pages)
        {
            PdfPageBuilder page = builder.AddPage(PageSize.A4);
            if (!string.IsNullOrEmpty(text))
            {
                page.AddText(text, 12, new PdfPoint(25, 700), font);
            }
        }

        return builder.Build();
    }
}
