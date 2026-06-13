using System.Diagnostics;
using System.IO;
using Lore.Agent.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Tests.Hosting;

public sealed class MemorydSupervisorTests : IDisposable
{
    private readonly List<MemorydSupervisor> _supervisors = [];

    public void Dispose()
    {
        foreach (MemorydSupervisor supervisor in _supervisors)
        {
            supervisor.Dispose();
        }
    }

    private static MemorydOptions FastOptions(string engine = "embedded") => new()
    {
        Engine = engine,
        RemoteUrl = engine == "remote" ? new Uri("http://remote.test:9999/") : null,
        HealthGateTimeout = TimeSpan.FromMilliseconds(500),
        HealthPollInterval = TimeSpan.FromMilliseconds(5),
        RestartDelay = TimeSpan.FromMilliseconds(5),
        DataDir = Path.Combine(Path.GetTempPath(), "lore-test-" + Guid.NewGuid().ToString("N")),
    };

    private MemorydSupervisor Create(MemorydOptions options, IProcessRunner runner, IMemorydHealthProbe probe)
    {
        var supervisor = new MemorydSupervisor(
            Options.Create(options), runner, probe, NullLogger<MemorydSupervisor>.Instance);
        _supervisors.Add(supervisor);
        return supervisor;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "condition was not met within the timeout");
    }

    [Fact]
    public async Task Embedded_spawns_then_signals_ready_after_health_goes_green()
    {
        var runner = new FakeProcessRunner();
        int polls = 0;
        var probe = new FakeHealthProbe(() => polls++ >= 2); // false, false, then healthy
        MemorydSupervisor supervisor = Create(FastOptions(), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.Ready.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(supervisor.Ready.IsCompletedSuccessfully);
        Assert.Single(runner.Started);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Embedded_restarts_after_an_unexpected_exit()
    {
        var runner = new FakeProcessRunner();
        var probe = new FakeHealthProbe(() => true);
        MemorydSupervisor supervisor = Create(FastOptions(), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(runner.Started);

        runner.Started[0].SignalExit(1); // memoryd "crashes"

        await WaitUntilAsync(() => runner.Started.Count >= 2, TimeSpan.FromSeconds(5));
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Stopping_kills_the_process_and_does_not_restart()
    {
        var runner = new FakeProcessRunner();
        var probe = new FakeHealthProbe(() => true);
        MemorydSupervisor supervisor = Create(FastOptions(), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        FakeManagedProcess process = runner.Started[0];

        await supervisor.StopAsync(CancellationToken.None);

        Assert.True(process.Killed);
        Assert.Single(runner.Started); // no restart after a clean stop
    }

    [Fact]
    public async Task Remote_engine_does_not_spawn_a_process()
    {
        var runner = new FakeProcessRunner();
        var probe = new FakeHealthProbe(() => true);
        MemorydSupervisor supervisor = Create(FastOptions("remote"), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);
        await supervisor.Ready.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(runner.Started);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Spawn_failure_is_caught_and_retried()
    {
        var runner = new FakeProcessRunner { ThrowsBeforeSuccess = 1 }; // first launch throws
        var probe = new FakeHealthProbe(() => true);
        MemorydSupervisor supervisor = Create(FastOptions(), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);

        // The service survives the launch failure and becomes ready on the retry.
        await supervisor.Ready.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(supervisor.Ready.IsCompletedSuccessfully);
        await supervisor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Unhealthy_within_timeout_restarts_without_signalling_ready()
    {
        var runner = new FakeProcessRunner();
        var probe = new FakeHealthProbe(() => false); // never becomes healthy
        MemorydSupervisor supervisor = Create(FastOptions(), runner, probe);

        await supervisor.StartAsync(CancellationToken.None);

        // gate times out and the supervisor restarts the sidecar
        await WaitUntilAsync(() => runner.Started.Count >= 2, TimeSpan.FromSeconds(5));
        Assert.False(supervisor.Ready.IsCompleted);
        await supervisor.StopAsync(CancellationToken.None);
    }
}
