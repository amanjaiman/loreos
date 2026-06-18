using System.ComponentModel;
using System.Diagnostics;
using Lore.Agent.Hosting;

namespace Lore.Agent.Tests.Hosting;

/// <summary>A controllable <see cref="IManagedProcess"/>: tests decide when it exits
/// and can observe whether it was killed.</summary>
internal sealed class FakeManagedProcess : IManagedProcess
{
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool HasExited { get; private set; }

    public int ExitCode { get; private set; }

    public bool Killed { get; private set; }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => _exited.Task.WaitAsync(cancellationToken);

    public void SignalExit(int code)
    {
        ExitCode = code;
        HasExited = true;
        _exited.TrySetResult();
    }

    public void Kill()
    {
        Killed = true;
        HasExited = true;
        _exited.TrySetResult();
    }

    public void Dispose()
    {
    }
}

/// <summary>Records every spawn and hands back a fake process per start.</summary>
internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly object _gate = new();
    private readonly List<FakeManagedProcess> _started = [];
    private readonly List<ProcessStartInfo> _startInfos = [];
    private int _attempts;

    /// <summary>Number of initial Start calls that throw (simulating a launch
    /// failure such as a missing interpreter) before one succeeds.</summary>
    public int ThrowsBeforeSuccess { get; set; }

    public IReadOnlyList<FakeManagedProcess> Started
    {
        get
        {
            lock (_gate)
            {
                return [.. _started];
            }
        }
    }

    /// <summary>The <see cref="ProcessStartInfo"/> of each spawn, so tests can assert
    /// which executable (packaged exe vs. python module) the supervisor launched.</summary>
    public IReadOnlyList<ProcessStartInfo> StartInfos
    {
        get
        {
            lock (_gate)
            {
                return [.. _startInfos];
            }
        }
    }

    public IManagedProcess Start(ProcessStartInfo startInfo)
    {
        if (Interlocked.Increment(ref _attempts) <= ThrowsBeforeSuccess)
        {
            throw new Win32Exception("simulated spawn failure");
        }

        var process = new FakeManagedProcess();
        lock (_gate)
        {
            _started.Add(process);
            _startInfos.Add(startInfo);
        }

        return process;
    }
}

/// <summary>Returns health on demand via a delegate (call-count aware).</summary>
internal sealed class FakeHealthProbe : IMemorydHealthProbe
{
    private readonly Func<bool> _isHealthy;

    public FakeHealthProbe(Func<bool> isHealthy) => _isHealthy = isHealthy;

    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken) => Task.FromResult(_isHealthy());
}
