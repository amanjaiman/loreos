using System.IO;
using Lore.Agent.Config;
using Lore.Agent.Hosting;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Tests.Hosting;

/// <summary>The file sink that backs <c>GET /system/log</c> (005 T005 follow-up): it must write
/// what <see cref="LogTail"/> reads back, honor a minimum level, and — once wrapped by the
/// redacting provider — never let a registered secret reach disk.</summary>
public sealed class FileLoggerProviderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "lore-log-tests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_dir, "lore.log");

    [Fact]
    public async Task Writes_a_line_that_the_log_tail_reads_back()
    {
        using (var provider = new FileLoggerProvider(LogPath))
        {
            provider.CreateLogger("Capture").LogInformation("memoryd ready");
        }

        IReadOnlyList<string> tail = await new LogTail(LogPath).ReadAsync();

        string line = Assert.Single(tail);
        Assert.Contains("memoryd ready", line, StringComparison.Ordinal);
        Assert.Contains("[INFO]", line, StringComparison.Ordinal);
        Assert.Contains("Capture", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Drops_entries_below_the_minimum_level()
    {
        using (var provider = new FileLoggerProvider(LogPath, LogLevel.Warning))
        {
            ILogger logger = provider.CreateLogger("Capture");
            logger.LogInformation("noisy detail");
            logger.LogWarning("memoryd restarted");
        }

        IReadOnlyList<string> tail = await new LogTail(LogPath).ReadAsync();

        string line = Assert.Single(tail);
        Assert.Contains("memoryd restarted", line, StringComparison.Ordinal);
        Assert.DoesNotContain("noisy detail", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrapped_by_the_redacting_provider_no_secret_reaches_disk()
    {
        var registry = new SecretRegistry();
        registry.Register("sk-live-9999");

        // The wrapper owns and disposes the inner sink; both carry `using` so the analyzer sees
        // deterministic disposal (the sink's second Dispose is a harmless no-op). The shared
        // read handle + per-line flush let the file be read while still open.
        using var inner = new FileLoggerProvider(LogPath);
        using var provider = new RedactingLoggerProvider(inner, registry);

        // A line that — through a bug — interpolated the key. The redactor must catch it.
        provider.CreateLogger("Provider").LogInformation("calling model with key sk-live-9999");

        // Read through LogTail (the real /system/log path), which opens shared so the still-open
        // writer doesn't cause a sharing violation.
        string contents = string.Join("\n", await new LogTail(LogPath).ReadAsync());

        Assert.DoesNotContain("sk-live-9999", contents, StringComparison.Ordinal);
        Assert.Contains(SecretRegistry.Placeholder, contents, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
