using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Lore.Agent.Hosting;

/// <summary>A minimal append-only file sink for the agent log (the spec 005 T005 follow-up):
/// writes one line per entry to <c>%LocalAppData%\Lore\lore.log</c> — the file
/// <see cref="LogTail"/> serves at <c>GET /system/log</c>. At composition it is wrapped by
/// <see cref="Lore.Agent.Config.RedactingLoggerProvider"/>, so every line is scrubbed of
/// registered secrets before it reaches disk (constitution §4.2); this sink itself never sees
/// the registry.
///
/// <para>The agent is a single personal process with modest log volume, so a lock-guarded
/// shared-write stream is simpler and sufficient — no rolling, no async batching. The file is
/// opened <see cref="FileShare.ReadWrite"/> so the concurrent tail reader is never blocked, and
/// flushed per line so a crash keeps the tail intact.</para></summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogSink _sink;
    private readonly LogLevel _minLevel;

    /// <param name="path">Absolute path to the log file; its directory is created if absent.</param>
    /// <param name="minLevel">The lowest level this sink records. Category-based filtering is
    /// still applied by the logging framework before a message reaches here.</param>
    public FileLoggerProvider(string path, LogLevel minLevel = LogLevel.Information)
    {
        _sink = new FileLogSink(path);
        _minLevel = minLevel;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _sink, _minLevel);

    public void Dispose() => _sink.Dispose();

    /// <summary>The lock-guarded writer shared by every category logger this provider hands out.</summary>
    private sealed class FileLogSink : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter _writer;

        public FileLogSink(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Append + shared read/write so LogTail reads concurrently; AutoFlush so the tail is
            // current even if the process dies. The StreamWriter owns (and disposes) the stream.
            _writer = new StreamWriter(
                new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
        }

        public void Write(string line)
        {
            lock (_gate)
            {
                _writer.WriteLine(line);
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer.Dispose();
            }
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly FileLogSink _sink;
        private readonly LogLevel _minLevel;

        public FileLogger(string category, FileLogSink sink, LogLevel minLevel)
        {
            _category = category;
            _sink = sink;
            _minLevel = minLevel;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= _minLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
            {
                return;
            }

            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
            {
                return;
            }

            // ISO-8601 UTC · fixed-width level · category · message — greppable and stable.
            var line = new StringBuilder()
                .Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                .Append(" [").Append(LevelLabel(logLevel)).Append("] ")
                .Append(_category).Append(" - ").Append(message);
            if (exception is not null)
            {
                line.Append(Environment.NewLine).Append(exception);
            }

            _sink.Write(line.ToString());
        }

        private static string LevelLabel(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE",
            LogLevel.Debug => "DEBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERROR",
            LogLevel.Critical => "CRIT",
            _ => level.ToString().ToUpperInvariant(),
        };
    }
}
