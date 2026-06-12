using System.Diagnostics;

namespace Lore.Agent.Hosting;

/// <summary>Launches child processes. Abstracted so the supervisor's spawn /
/// health-gate / restart logic is testable without a real Python process.</summary>
public interface IProcessRunner
{
    IManagedProcess Start(ProcessStartInfo startInfo);
}

/// <summary>A running child process the supervisor can await and terminate.</summary>
public interface IManagedProcess : IDisposable
{
    bool HasExited { get; }

    int ExitCode { get; }

    /// <summary>Completes when the process exits (or the token is cancelled).</summary>
    Task WaitForExitAsync(CancellationToken cancellationToken);

    /// <summary>Terminate the process and its tree.</summary>
    void Kill();
}
