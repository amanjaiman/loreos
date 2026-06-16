namespace Lore.Agent.Capture;

/// <summary>The single Win32/UI-Automation seam for reading a window's text
/// (constitution §3.2). Every way the agent pulls text off the screen — UIA, OCR, or a
/// fallback composite of them — implements this one interface, so the capture loop never
/// touches the platform directly and the extraction strategy stays swappable.</summary>
public interface ITextExtractor
{
    /// <summary>Extract whatever text the window exposes. Returns
    /// <see cref="ExtractedText.Empty"/> when there is nothing to read — never throws for
    /// an ordinary unreadable window, so a single bad extraction can't kill the loop.</summary>
    Task<ExtractedText> ExtractAsync(WindowSnapshot window, CancellationToken cancellationToken = default);
}
