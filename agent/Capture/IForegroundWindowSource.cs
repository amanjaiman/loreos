namespace Lore.Agent.Capture;

/// <summary>The single Win32 seam for reading the foreground window (constitution
/// §3.2). The only P/Invoke for window identity lives behind this interface, so
/// <see cref="WindowMonitor"/>'s dwell and change-detection logic is testable without
/// a live desktop.</summary>
public interface IForegroundWindowSource
{
    /// <summary>The window in the foreground right now, or <see cref="WindowSnapshot.None"/>
    /// when there is none the agent can read (locked screen, no focus, a read failure).</summary>
    WindowSnapshot Current();
}
