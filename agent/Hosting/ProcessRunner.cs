using System.Diagnostics;

namespace Lore.Agent.Hosting;

/// <summary>Real <see cref="IProcessRunner"/> over <see cref="Process"/>.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public IManagedProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        return new ManagedProcess(process);
    }

    private sealed class ManagedProcess : IManagedProcess
    {
        private readonly Process _process;

        public ManagedProcess(Process process) => _process = process;

        public bool HasExited => _process.HasExited;

        public int ExitCode => _process.ExitCode;

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _process.WaitForExitAsync(cancellationToken);

        public void Kill() => _process.Kill(entireProcessTree: true);

        public void Dispose() => _process.Dispose();
    }
}
