using System.Windows.Automation;

namespace Lore.Agent.Capture;

/// <summary>The UI Automation implementation of the structural security layer: reports a
/// window as protected when the focused control is a password field. Trust-critical and
/// <b>fail-closed</b> — if UIA throws or can't be read, it answers "protected" so an
/// unreadable secure surface is dropped rather than captured (constitution §4.4).
///
/// <para>Platform glue behind <see cref="IWindowSecurityProbe"/>; the chain logic that
/// consumes it is unit-tested with a fake probe.</para></summary>
public sealed class UiaWindowSecurityProbe : IWindowSecurityProbe
{
    public bool HasProtectedContent(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsEmpty)
        {
            return false; // no window to protect
        }

        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            return focused is not null && focused.Current.IsPassword;
        }
        catch (ElementNotAvailableException)
        {
            return true; // can't read focus → assume protected
        }
#pragma warning disable CA1031 // UIA throws assorted COM errors; any uncertainty must fail closed
        catch (Exception)
#pragma warning restore CA1031
        {
            return true;
        }
    }
}
