namespace Lore.Agent.Capture;

/// <summary>Which extractor produced a window's text. Recorded so the activity log
/// (T007) and metrics can show <em>how</em> a capture was read, and so a window read by
/// OCR can be treated differently from one with a clean accessibility tree.</summary>
public enum ExtractionSource
{
    /// <summary>No extractor produced any text.</summary>
    None,

    /// <summary>Read from the window's UI Automation tree — the clean, primary path.</summary>
    UiAutomation,

    /// <summary>Recognized from a screen capture because UIA exposed no text.</summary>
    Ocr,
}

/// <summary>Text pulled from a window by an <see cref="ITextExtractor"/>, tagged with
/// its source. This is raw, <em>unfiltered</em> text — it must pass the sensitivity
/// filter chain (T003) before anything analyzes, stores, or transmits it.</summary>
public sealed record ExtractedText(string Text, ExtractionSource Source)
{
    /// <summary>Nothing was extracted.</summary>
    public static readonly ExtractedText Empty = new(string.Empty, ExtractionSource.None);

    /// <summary><c>true</c> when no usable text was produced (null, empty, or whitespace).</summary>
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
