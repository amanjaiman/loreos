using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lore.Agent.Hosting;

/// <summary>Supervises the memoryd sidecar: spawns it, gates on <c>/health</c> before
/// declaring readiness, restarts it if it dies, and shuts it down cleanly with the
/// agent (constitution §3.3). In <c>remote</c> mode it spawns nothing and only gates
/// on the remote endpoint.</summary>
public sealed class MemorydSupervisor : BackgroundService
{
    private readonly MemorydOptions _options;
    private readonly IProcessRunner _runner;
    private readonly IMemorydHealthProbe _health;
    private readonly ILogger<MemorydSupervisor> _logger;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MemorydSupervisor(
        IOptions<MemorydOptions> options,
        IProcessRunner runner,
        IMemorydHealthProbe health,
        ILogger<MemorydSupervisor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _runner = runner;
        _health = health;
        _logger = logger;
    }

    /// <summary>Completes once memoryd is healthy. Services that touch memory await
    /// this before their first call.</summary>
    public Task Ready => _ready.Task;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _ready.TrySetCanceled(CancellationToken.None);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEmbedded)
        {
            await SuperviseRemoteAsync(stoppingToken).ConfigureAwait(false);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            IManagedProcess? process = null;
            Task? exited = null;
            try
            {
                // Spawn inside the try: a launch failure (interpreter or exe missing)
                // must be logged and retried, never crash the background service.
                process = _runner.Start(BuildStartInfo());
                exited = process.WaitForExitAsync(stoppingToken);

                // Race the health gate against process exit so a startup crash (port in
                // use, import error) is detected immediately rather than after the full
                // health-gate timeout.
                bool healthy = await WaitForHealthyAsync(stoppingToken, exited).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                if (healthy)
                {
                    _logger.LogInformation("memoryd is healthy at {BaseAddress}", _options.BaseAddress);
                    _ready.TrySetResult();
                    await exited.ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _logger.LogWarning(
                        "memoryd exited unexpectedly (code {ExitCode}); restarting", SafeExitCode(process));
                }
                else if (process.HasExited)
                {
                    _logger.LogWarning(
                        "memoryd exited during startup (code {ExitCode}); restarting", SafeExitCode(process));
                }
                else
                {
                    _logger.LogWarning(
                        "memoryd did not become healthy within {Timeout}; restarting", _options.HealthGateTimeout);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
            {
                _logger.LogError(ex, "failed to launch memoryd; retrying in {Delay}", _options.RestartDelay);
            }
            finally
            {
                if (process is not null)
                {
                    TryKill(process);
                    process.Dispose();
                }

                await ObserveExitedAsync(exited).ConfigureAwait(false);
            }

            await DelaySafe(_options.RestartDelay, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("memoryd supervisor stopped");
    }

    private async Task SuperviseRemoteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("memory engine is remote ({Url}); not spawning a sidecar", _options.BaseAddress);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await WaitForHealthyAsync(stoppingToken).ConfigureAwait(false))
            {
                _logger.LogInformation("remote memoryd is healthy at {BaseAddress}", _options.BaseAddress);
                _ready.TrySetResult();
                return;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            // Don't return silently: keep retrying so a remote that comes up later still
            // satisfies Ready, and never leave the gate unsignalled without a log.
            _logger.LogWarning(
                "remote memoryd at {Url} not healthy within {Timeout}; retrying",
                _options.BaseAddress,
                _options.HealthGateTimeout);
            await DelaySafe(_options.RestartDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitForHealthyAsync(CancellationToken stoppingToken, Task? exited = null)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(_options.HealthGateTimeout);
        CancellationToken token = timeoutCts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (exited is { IsCompleted: true })
                {
                    return false; // the process died during the gate
                }

                if (await _health.IsHealthyAsync(token).ConfigureAwait(false))
                {
                    return true;
                }

                Task delay = Task.Delay(_options.HealthPollInterval, token);
                if (exited is null)
                {
                    await delay.ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAny(delay, exited).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // timed out or the agent is shutting down
        }

        return false;
    }

    private ProcessStartInfo BuildStartInfo()
    {
        ProcessStartInfo info;
        string? packaged = _options.PackagedExecutable;
        if (!string.IsNullOrEmpty(packaged) && File.Exists(packaged))
        {
            info = new ProcessStartInfo(packaged);
        }
        else
        {
            info = new ProcessStartInfo(_options.PythonExecutable);
            info.ArgumentList.Add("-m");
            info.ArgumentList.Add("lore_memoryd");
        }

        info.UseShellExecute = false;
        info.Environment["LORE_MEMORYD_HOST"] = _options.Host;
        info.Environment["LORE_MEMORYD_PORT"] = _options.Port.ToString(CultureInfo.InvariantCulture);
        return info;
    }

    private static async Task ObserveExitedAsync(Task? exited)
    {
        if (exited is null)
        {
            return;
        }

        try
        {
            await exited.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
#pragma warning disable CA1031
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private void TryKill(IManagedProcess process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (InvalidOperationException)
        {
            // already gone
        }
        catch (Win32Exception ex)
        {
            _logger.LogDebug(ex, "failed to kill memoryd");
        }
    }

    private static int SafeExitCode(IManagedProcess process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }

    private static async Task DelaySafe(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }
}
