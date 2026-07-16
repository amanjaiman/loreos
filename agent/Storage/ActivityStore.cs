using System.Globalization;
using System.Text.Json;
using Lore.Agent.Capture.Episodes;
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

    /// <summary>Persist one closed episode (v2-001). List-valued fields are stored as
    /// JSON arrays; the row is the auditable record of what the distiller will see.</summary>
    public Task SaveEpisodeAsync(Episode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return ExecuteAsync(
            command =>
            {
                command.CommandText =
                    """
                    INSERT INTO episodes (id, started_at, ended_at, executables, titles, samples, observation_count)
                    VALUES ($id, $start, $end, $exes, $titles, $samples, $count);
                    """;
                command.Parameters.AddWithValue("$id", episode.Id);
                command.Parameters.AddWithValue("$start", Iso(episode.StartedAt));
                command.Parameters.AddWithValue("$end", Iso(episode.EndedAt));
                command.Parameters.AddWithValue("$exes", JsonSerializer.Serialize(episode.Executables));
                command.Parameters.AddWithValue("$titles", JsonSerializer.Serialize(episode.Titles));
                command.Parameters.AddWithValue("$samples", JsonSerializer.Serialize(episode.Samples));
                command.Parameters.AddWithValue("$count", episode.ObservationCount);
            },
            cancellationToken);
    }

    /// <summary>The most recent episodes, newest first.</summary>
    public async Task<IReadOnlyList<Episode>> GetRecentEpisodesAsync(
        int limit = 50, CancellationToken cancellationToken = default)
    {
        var results = new List<Episode>();
        await ReadAsync(
            command => command.CommandText =
                """
                SELECT id, started_at, ended_at, executables, titles, samples, observation_count
                FROM episodes ORDER BY ended_at DESC LIMIT $limit;
                """,
            limit,
            reader => results.Add(new Episode(
                reader.GetString(0),
                ParseAt(reader.GetString(1)),
                ParseAt(reader.GetString(2)),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? [],
                reader.GetInt32(6))),
            cancellationToken).ConfigureAwait(false);
        return results;
    }

    /// <summary>One episode by id, or <c>null</c> — provenance lookups from the library.</summary>
    public async Task<Episode?> GetEpisodeAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        Episode? found = null;
        await ReadAsync(
            command =>
            {
                command.CommandText =
                    """
                    SELECT id, started_at, ended_at, executables, titles, samples, observation_count
                    FROM episodes WHERE id = $id LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$id", id);
            },
            1,
            reader => found = new Episode(
                reader.GetString(0),
                ParseAt(reader.GetString(1)),
                ParseAt(reader.GetString(2)),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(4)) ?? [],
                JsonSerializer.Deserialize<List<string>>(reader.GetString(5)) ?? [],
                reader.GetInt32(6)),
            cancellationToken).ConfigureAwait(false);
        return found;
    }

    /// <summary>Append one decision-trail row (v2-001).</summary>
    public Task LogDecisionAsync(DecisionEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ExecuteAsync(
            command =>
            {
                command.CommandText =
                    """
                    INSERT INTO decisions (at, episode_id, action, reason, statement, kind, memory_id)
                    VALUES ($at, $episode, $action, $reason, $statement, $kind, $memory);
                    """;
                command.Parameters.AddWithValue("$at", Iso(entry.At));
                command.Parameters.AddWithValue("$episode", entry.EpisodeId);
                command.Parameters.AddWithValue("$action", entry.Action);
                command.Parameters.AddWithValue("$reason", entry.Reason);
                command.Parameters.AddWithValue("$statement", entry.Statement);
                command.Parameters.AddWithValue("$kind", entry.Kind);
                command.Parameters.AddWithValue("$memory", entry.MemoryId);
            },
            cancellationToken);
    }

    /// <summary>Count decision rows with the given action at or after <paramref name="since"/> —
    /// the promotion budget's daily counter (v2-001 T006).</summary>
    public async Task<int> CountDecisionsSinceAsync(
        string action, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(action);
        int count = 0;
        await ReadAsync(
            command =>
            {
                command.CommandText =
                    """
                    SELECT COUNT(*) FROM decisions
                    WHERE action = $action AND at >= $since LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$action", action);
                command.Parameters.AddWithValue("$since", Iso(since));
            },
            1,
            reader => count = reader.GetInt32(0),
            cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <summary>Decision counts grouped by action at or after <paramref name="since"/> —
    /// the capture-economy line (episodes seen → distilled → staged → promoted…).</summary>
    public async Task<IReadOnlyDictionary<string, int>> GetDecisionCountsSinceAsync(
        DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await ReadAsync(
            command =>
            {
                command.CommandText =
                    """
                    SELECT action, COUNT(*) FROM decisions
                    WHERE at >= $since GROUP BY action LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$since", Iso(since));
            },
            1000,
            reader => counts[reader.GetString(0)] = reader.GetInt32(1),
            cancellationToken).ConfigureAwait(false);
        return counts;
    }

    /// <summary>The most recent decision-trail rows, newest first.</summary>
    public async Task<IReadOnlyList<DecisionEntry>> GetRecentDecisionsAsync(
        int limit = 100, CancellationToken cancellationToken = default)
    {
        var results = new List<DecisionEntry>();
        await ReadAsync(
            command => command.CommandText =
                """
                SELECT at, episode_id, action, reason, statement, kind, memory_id
                FROM decisions ORDER BY id DESC LIMIT $limit;
                """,
            limit,
            reader => results.Add(new DecisionEntry(
                ParseAt(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
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
            CREATE TABLE IF NOT EXISTS episodes (
                id TEXT PRIMARY KEY,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                executables TEXT NOT NULL,
                titles TEXT NOT NULL,
                samples TEXT NOT NULL,
                observation_count INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS decisions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at TEXT NOT NULL,
                episode_id TEXT NOT NULL,
                action TEXT NOT NULL,
                reason TEXT NOT NULL,
                statement TEXT NOT NULL,
                kind TEXT NOT NULL,
                memory_id TEXT NOT NULL
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
