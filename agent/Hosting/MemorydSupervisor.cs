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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsEmbedded)
        {
            _logger.LogInformation("memory engine is remote ({Url}); not spawning a sidecar", _options.BaseAddress);
            if (await WaitForHealthyAsync(stoppingToken).ConfigureAwait(false))
            {
                _ready.TrySetResult();
            }

            return;
        }

        Directory.CreateDirectory(_options.DataDir);
        while (!stoppingToken.IsCancellationRequested)
        {
            using IManagedProcess process = _runner.Start(BuildStartInfo());
            try
            {
                if (await WaitForHealthyAsync(stoppingToken).ConfigureAwait(false))
                {
                    _logger.LogInformation("memoryd is healthy at {BaseAddress}", _options.BaseAddress);
                    _ready.TrySetResult();
                    await process.WaitForExitAsync(stoppingToken).ConfigureAwait(false);
                    if (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }

                    _logger.LogWarning(
                        "memoryd exited unexpectedly (code {ExitCode}); restarting", SafeExitCode(process));
                }
                else if (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "memoryd did not become healthy within {Timeout}; restarting", _options.HealthGateTimeout);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            finally
            {
                TryKill(process);
            }

            await DelaySafe(_options.RestartDelay, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("memoryd supervisor stopped");
    }

    private async Task<bool> WaitForHealthyAsync(CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(_options.HealthGateTimeout);
        CancellationToken token = timeoutCts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (await _health.IsHealthyAsync(token).ConfigureAwait(false))
                {
                    return true;
                }

                await Task.Delay(_options.HealthPollInterval, token).ConfigureAwait(false);
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
        info.Environment["LORE_MEMORYD_DATA_DIR"] = _options.DataDir;
        return info;
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
