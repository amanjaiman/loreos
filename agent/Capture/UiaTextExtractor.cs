using System.Text;
using System.Windows.Automation;

namespace Lore.Agent.Capture;

/// <summary>Reads a window's text from its UI Automation tree — the primary, highest
/// quality extraction path (clean strings, no pixels). Like the Win32 window source, it
/// is deliberately total: any UIA failure (an element going away mid-walk, a process
/// that doesn't answer) yields <see cref="ExtractedText.Empty"/> so the loop falls back
/// to OCR rather than throwing. The traversal is bounded (node and character caps) so a
/// pathological tree can't stall a poll tick.
///
/// <para>This is platform glue that cannot run headlessly; the tested logic lives in
/// <see cref="CompositeTextExtractor"/>. The UIA call surface is kept to a tight,
/// auditable minimum here.</para></summary>
public sealed class UiaTextExtractor : ITextExtractor
{
    // Caps that keep a single extraction bounded regardless of tree shape. Sized to hold
    // a screenful of meaningful text without walking enormous virtualized lists.
    private const int MaxNodes = 1500;
    private const int MaxChars = 20_000;

    public Task<ExtractedText> ExtractAsync(
        WindowSnapshot window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsEmpty)
        {
            return Task.FromResult(ExtractedText.Empty);
        }

        // UIA is synchronous COM; run it inline and hand back a completed task. The caller
        // (capture loop) is already off any UI thread.
        try
        {
            AutomationElement? root = AutomationElement.FromHandle(new IntPtr(window.Handle));
            if (root is null)
            {
                return Task.FromResult(ExtractedText.Empty);
            }

            string text = ReadTree(root, cancellationToken);
            return Task.FromResult(
                string.IsNullOrWhiteSpace(text)
                    ? ExtractedText.Empty
                    : new ExtractedText(text, ExtractionSource.UiAutomation));
        }
        catch (ElementNotAvailableException)
        {
            return Task.FromResult(ExtractedText.Empty); // the window changed or closed mid-read
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // UIA surfaces a wide range of COM errors; none should kill the loop
        catch (Exception)
#pragma warning restore CA1031
        {
            return Task.FromResult(ExtractedText.Empty);
        }
    }

    private static string ReadTree(AutomationElement root, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        int visited = 0;
        TreeWalker walker = TreeWalker.ContentViewWalker;

        while (queue.Count > 0 && visited < MaxNodes && builder.Length < MaxChars)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AutomationElement element = queue.Dequeue();
            visited++;

            AppendText(builder, element);

            try
            {
                AutomationElement? child = walker.GetFirstChild(element);
                while (child is not null)
                {
                    queue.Enqueue(child);
                    child = walker.GetNextSibling(child);
                }
            }
            catch (ElementNotAvailableException)
            {
                // this subtree vanished; keep what we have and move on
            }
        }

        return builder.ToString().Trim();
    }

    private static void AppendText(StringBuilder builder, AutomationElement element)
    {
        try
        {
            // Prefer a control's value (text boxes, documents); fall back to its name.
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out object? valueObj)
                && valueObj is ValuePattern value
                && !string.IsNullOrWhiteSpace(value.Current.Value))
            {
                AppendLine(builder, value.Current.Value);
                return;
            }

            string name = element.Current.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                AppendLine(builder, name);
            }
        }
        catch (ElementNotAvailableException)
        {
            // element disappeared between enqueue and read; skip it
        }
    }

    private static void AppendLine(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        builder.Append(value.Trim());
    }
}
