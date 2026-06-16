namespace Lore.Agent.Capture;

/// <summary>The UI Automation seam for the structural layer of the sensitivity chain
/// (constitution §3.2): does this window currently expose protected content — a focused
/// password field or a window flagged secure? Kept behind an interface so
/// <see cref="SensitivityFilter"/> stays pure and the trust-critical chain is testable
/// without a live desktop.</summary>
public interface IWindowSecurityProbe
{
    /// <summary><c>true</c> when the window exposes protected content and must not be
    /// captured. Implementations fail closed: if they cannot determine safety, they
    /// answer <c>true</c>.</summary>
    bool HasProtectedContent(WindowSnapshot window);
}
