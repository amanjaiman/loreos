using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Lore.Agent.Storage;

/// <summary>The thin, local SQLite store for capture telemetry (spec 003 T007): a
/// human-readable <c>activity_log</c> of what Lore did with each window, and a
/// <c>raw_captures</c> table of the text it actually read. This is <b>operational data the
/// user can inspect, not memory</b> — it is deliberately isolated from
/// <c>IMemoryService</c> and <c>IInferenceBackend</c> and never crosses those seams
/// (acceptance criterion 5; "the code is the audit trail"). The only thing this class
/// touches is its own SQLite file.
///
/// <para>One connection is held open for the store's lifetime (so <c>:memory:</c> works in
/// tests), and writes are serialized — the capture loop is single-threaded but a lock keeps
/// it safe regardless.</para></summary>
public sealed class ActivityStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="dataSource">A file path for the real store, or <c>:memory:</c> for
    /// tests.</param>
    public ActivityStore(string dataSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSource);
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dataSource }.ToString();
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        EnsureSchema();
    }

    /// <summary>Append a decision to the activity log.</summary>
    public Task LogActivityAsync(ActivityLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync(
            command =>
            {
                command.CommandText =
                    """
                    INSERT INTO activity_log (at, executable, window_title, decision, reason, observation, category)
                    VALUES ($at, $exe, $title, $decision, $reason, $observation, $category);
                    """;
                command.Parameters.AddWithValue("$at", Iso(entry.At));
                command.Parameters.AddWithValue("$exe", entry.Executable);
                command.Parameters.AddWithValue("$title", entry.WindowTitle);
                command.Parameters.AddWithValue("$decision", entry.Decision.ToString());
                command.Parameters.AddWithValue("$reason", entry.Reason);
                command.Parameters.AddWithValue("$observation", entry.Observation);
                command.Parameters.AddWithValue("$category", entry.Category);
            },
            cancellationToken);
    }

    /// <summary>Append a raw capture to the local telemetry table.</summary>
    public Task LogRawCaptureAsync(RawCaptureEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync(
            command =>
            {
                command.CommandText =
                    """
                    INSERT INTO raw_captures (at, executable, window_title, extraction_source, content_type, text_length, text)
                    VALUES ($at, $exe, $title, $source, $type, $len, $text);
                    """;
                command.Parameters.AddWithValue("$at", Iso(entry.At));
                command.Parameters.AddWithValue("$exe", entry.Executable);
                command.Parameters.AddWithValue("$title", entry.WindowTitle);
                command.Parameters.AddWithValue("$source", entry.ExtractionSource);
                command.Parameters.AddWithValue("$type", entry.ContentType);
                command.Parameters.AddWithValue("$len", entry.Text.Length);
                command.Parameters.AddWithValue("$text", entry.Text);
            },
            cancellationToken);
    }

    /// <summary>The most recent activity-log rows, newest first.</summary>
    public async Task<IReadOnlyList<ActivityLogEntry>> GetRecentActivityAsync(
        int limit = 50, CancellationToken cancellationToken = default)
    {
        var results = new List<ActivityLogEntry>();
        await ReadAsync(
            command => command.CommandText =
                """
                SELECT at, executable, window_title, decision, reason, observation, category
                FROM activity_log ORDER BY id DESC LIMIT $limit;
                """,
            limit,
            reader => results.Add(new ActivityLogEntry(
                ParseAt(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<ActivityDecision>(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6))),
            cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>The most recent raw-capture rows, newest first.</summary>
    public async Task<IReadOnlyList<RawCaptureEntry>> GetRecentRawCapturesAsync(
        int limit = 50, CancellationToken cancellationToken = default)
    {
        var results = new List<RawCaptureEntry>();
        await ReadAsync(
            command => command.CommandText =
                """
                SELECT at, executable, window_title, extraction_source, content_type, text
                FROM raw_captures ORDER BY id DESC LIMIT $limit;
                """,
            limit,
            reader => results.Add(new RawCaptureEntry(
                ParseAt(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5))),
            cancellationToken).ConfigureAwait(false);
        return results;
    }

    public void Dispose()
    {
        _connection.Dispose();
        _gate.Dispose();
    }

    private void EnsureSchema()
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS activity_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at TEXT NOT NULL,
                executable TEXT NOT NULL,
                window_title TEXT NOT NULL,
                decision TEXT NOT NULL,
                reason TEXT NOT NULL,
                observation TEXT NOT NULL,
                category TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS raw_captures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at TEXT NOT NULL,
                executable TEXT NOT NULL,
                window_title TEXT NOT NULL,
                extraction_source TEXT NOT NULL,
                content_type TEXT NOT NULL,
                text_length INTEGER NOT NULL,
                text TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    // Helpers take a configure callback that sets CommandText (always a constant literal at
    // the call site, so CA2100 is satisfied) and binds parameters; user data only ever
    // arrives through SqliteParameters, never string-concatenated into SQL.
    private async Task ExecuteAsync(
        Action<SqliteCommand> configure, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            configure(command);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReadAsync(
        Action<SqliteCommand> configure, int limit, Action<SqliteDataReader> onRow,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            configure(command);
            command.Parameters.AddWithValue("$limit", limit);
            using SqliteDataReader reader =
                await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                onRow(reader);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Iso(DateTimeOffset at) =>
        at.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseAt(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
