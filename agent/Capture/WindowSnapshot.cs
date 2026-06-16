namespace Lore.Agent.Capture;

/// <summary>The identity of a foreground window at one poll tick, read through the
/// Win32 seam (<see cref="IForegroundWindowSource"/>). Identity is the window handle,
/// its owning executable, and its title — text <em>content</em> is deliberately not
/// part of identity: it is extracted later (spec 003 T002) and diffed by the gates
/// (T004/T005), long after the monitor has decided the window is worth looking at.</summary>
public sealed record WindowSnapshot(long Handle, string ProcessExecutable, string Title)
{
    /// <summary>No foreground window — a locked screen, the desktop, or focus the
    /// agent cannot read. Distinct from a real window that merely has an empty title.</summary>
    public static readonly WindowSnapshot None = new(0, string.Empty, string.Empty);

    /// <summary><c>true</c> when there is no readable foreground window.</summary>
    public bool IsEmpty => Handle == 0;
}
