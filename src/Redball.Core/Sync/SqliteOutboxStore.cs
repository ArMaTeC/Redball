namespace Redball.Core.Sync;

using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// SQLite-backed implementation of the outbox store.
/// Provides durable, indexed storage for sync events.
/// </summary>
public sealed class SqliteOutboxStore : IOutboxStore, IDisposable
{
    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// Creates a new SQLite outbox store.
    /// </summary>
    /// <param name="dbPath">Path to SQLite database file. Defaults to LocalAppData.</param>
    public SqliteOutboxStore(string? dbPath = null)
    {
        _dbPath = dbPath ?? GetDefaultDbPath();
        _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;Foreign Keys=True";
    }

    private static string GetDefaultDbPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var redballDir = Path.Combine(localAppData, "Redball", "UserData");
        Directory.CreateDirectory(redballDir);
        return Path.Combine(redballDir, "sync_outbox.db");
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);

            // Create events table with indexes
            var createTableSql = @"
                CREATE TABLE IF NOT EXISTS SyncEvents (
                    EventId TEXT PRIMARY KEY,
                    AggregateId TEXT NOT NULL,
                    AggregateVersion INTEGER NOT NULL,
                    EventType TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    RetryCount INTEGER NOT NULL DEFAULT 0,
                    NextAttemptUtc TEXT NOT NULL,
                    Status INTEGER NOT NULL DEFAULT 0,
                    LastError TEXT,
                    LastAttemptUtc TEXT
                );

                CREATE INDEX IF NOT EXISTS IX_SyncEvents_Status ON SyncEvents(Status);
                CREATE INDEX IF NOT EXISTS IX_SyncEvents_NextAttempt ON SyncEvents(NextAttemptUtc);
                CREATE INDEX IF NOT EXISTS IX_SyncEvents_Aggregate ON SyncEvents(AggregateId, AggregateVersion);
                CREATE INDEX IF NOT EXISTS IX_SyncEvents_Created ON SyncEvents(CreatedUtc);
            ";

            using var cmd = new SqliteCommand(createTableSql, connection);
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnqueueAsync(SyncEvent evt, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            INSERT INTO SyncEvents (EventId, AggregateId, AggregateVersion, EventType, PayloadJson, CreatedUtc, RetryCount, NextAttemptUtc, Status, LastError, LastAttemptUtc)
            VALUES (@EventId, @AggregateId, @AggregateVersion, @EventType, @PayloadJson, @CreatedUtc, @RetryCount, @NextAttemptUtc, @Status, @LastError, @LastAttemptUtc)
            ON CONFLICT(EventId) DO NOTHING;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@EventId", evt.EventId.ToString());
        cmd.Parameters.AddWithValue("@AggregateId", evt.AggregateId);
        cmd.Parameters.AddWithValue("@AggregateVersion", evt.AggregateVersion);
        cmd.Parameters.AddWithValue("@EventType", evt.EventType);
        cmd.Parameters.AddWithValue("@PayloadJson", evt.PayloadJson);
        cmd.Parameters.AddWithValue("@CreatedUtc", evt.CreatedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@RetryCount", evt.RetryCount);
        cmd.Parameters.AddWithValue("@NextAttemptUtc", evt.NextAttemptUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@Status", (int)evt.Status);
        cmd.Parameters.AddWithValue("@LastError", (object?)evt.LastError ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@LastAttemptUtc", (object?)evt.LastAttemptUtc?.ToString("O") ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<SyncEvent>> DequeueBatchAsync(int max, DateTime utcNow, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Begin transaction for atomic dequeue
        using var transaction = connection.BeginTransaction();

        // Select events ready for processing
        const string selectSql = @"
            SELECT EventId, AggregateId, AggregateVersion, EventType, PayloadJson, CreatedUtc, RetryCount, NextAttemptUtc, Status, LastError, LastAttemptUtc
            FROM SyncEvents
            WHERE Status = @PendingStatus AND NextAttemptUtc <= @Now
            ORDER BY NextAttemptUtc ASC, AggregateVersion ASC
            LIMIT @Limit;";

        using var selectCmd = new SqliteCommand(selectSql, connection, transaction);
        selectCmd.Parameters.AddWithValue("@PendingStatus", (int)SyncEventStatus.Pending);
        selectCmd.Parameters.AddWithValue("@Now", utcNow.ToString("O"));
        selectCmd.Parameters.AddWithValue("@Limit", max);

        var events = new List<SyncEvent>();
        var eventIds = new List<string>();

        using var reader = await selectCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var evt = ReadEventFromReader(reader);
            events.Add(evt);
            eventIds.Add(evt.EventId.ToString());
        }

        // Mark selected events as in-flight
        if (eventIds.Count > 0)
        {
            var inFlightSql = $@"
                UPDATE SyncEvents
                SET Status = @InFlightStatus, LastAttemptUtc = @Now
                WHERE EventId IN ({string.Join(", ", eventIds.Select((_, i) => $"@id{i}"))});";

            using var updateCmd = new SqliteCommand(inFlightSql, connection, transaction);
            updateCmd.Parameters.AddWithValue("@InFlightStatus", (int)SyncEventStatus.InFlight);
            updateCmd.Parameters.AddWithValue("@Now", utcNow.ToString("O"));

            for (int i = 0; i < eventIds.Count; i++)
            {
                updateCmd.Parameters.AddWithValue($"@id{i}", eventIds[i]);
            }

            await updateCmd.ExecuteNonQueryAsync(ct);
        }

        transaction.Commit();
        return events;
    }

    public async Task MarkSucceededAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            UPDATE SyncEvents
            SET Status = @CompletedStatus, LastError = NULL, LastAttemptUtc = @Now
            WHERE EventId = @EventId;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)SyncEventStatus.Completed);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@EventId", eventId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkFailedAsync(Guid eventId, string reason, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        // First get current event to calculate next retry
        const string selectSql = "SELECT * FROM SyncEvents WHERE EventId = @EventId;";
        using var selectCmd = new SqliteCommand(selectSql, connection);
        selectCmd.Parameters.AddWithValue("@EventId", eventId.ToString());

        using var reader = await selectCmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return;

        var evt = ReadEventFromReader(reader);
        reader.Close();

        // Calculate retry backoff
        var updated = evt.WithRetryAttempt(reason);

        const string updateSql = @"
            UPDATE SyncEvents
            SET RetryCount = @RetryCount, NextAttemptUtc = @NextAttemptUtc, Status = @Status, LastError = @LastError, LastAttemptUtc = @LastAttemptUtc
            WHERE EventId = @EventId;";

        using var updateCmd = new SqliteCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("@RetryCount", updated.RetryCount);
        updateCmd.Parameters.AddWithValue("@NextAttemptUtc", updated.NextAttemptUtc.ToString("O"));
        updateCmd.Parameters.AddWithValue("@Status", (int)updated.Status);
        updateCmd.Parameters.AddWithValue("@LastError", (object?)updated.LastError ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("@LastAttemptUtc", updated.LastAttemptUtc?.ToString("O") ?? DBNull.Value);
        updateCmd.Parameters.AddWithValue("@EventId", eventId.ToString());

        await updateCmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDeadLetterAsync(Guid eventId, string reason, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            UPDATE SyncEvents
            SET Status = @DeadLetterStatus, LastError = @LastError, LastAttemptUtc = @Now
            WHERE EventId = @EventId;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@DeadLetterStatus", (int)SyncEventStatus.DeadLetter);
        cmd.Parameters.AddWithValue("@LastError", reason);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@EventId", eventId.ToString());

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> GetQueueDepthAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = "SELECT COUNT(*) FROM SyncEvents WHERE Status IN (0, 1);";
        using var cmd = new SqliteCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<TimeSpan?> GetOldestPendingAgeAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = "SELECT MIN(CreatedUtc) FROM SyncEvents WHERE Status IN (0, 1);";
        using var cmd = new SqliteCommand(sql, connection);
        var result = await cmd.ExecuteScalarAsync(ct);

        if (result == null || result == DBNull.Value) return null;

        var oldest = DateTime.Parse((string)result);
        return DateTime.UtcNow - oldest;
    }

    public async Task<SyncStatistics> GetStatisticsAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            SELECT 
                COUNT(*) as Total,
                SUM(CASE WHEN Status = 0 THEN 1 ELSE 0 END) as Pending,
                SUM(CASE WHEN Status = 1 THEN 1 ELSE 0 END) as InFlight,
                SUM(CASE WHEN Status = 2 THEN 1 ELSE 0 END) as Completed,
                SUM(CASE WHEN Status = 3 THEN 1 ELSE 0 END) as DeadLetter,
                AVG(RetryCount) as AvgRetries,
                MAX(CASE WHEN Status = 2 THEN LastAttemptUtc END) as LastSuccess
            FROM SyncEvents;";

        using var cmd = new SqliteCommand(sql, connection);
        using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct)) return SyncStatistics.Empty;

        var total = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
        var pending = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
        var inFlight = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        var completed = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
        var deadLetter = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
        var avgRetries = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);

        DateTime? lastSuccess = null;
        if (!reader.IsDBNull(6))
        {
            lastSuccess = DateTime.Parse(reader.GetString(6));
        }

        var oldestAge = await GetOldestPendingAgeAsync(ct);

        return new SyncStatistics(
            total, pending, inFlight, completed, deadLetter,
            oldestAge, lastSuccess, avgRetries);
    }

    public async Task<IReadOnlyList<SyncEvent>> GetEventsByStatusAsync(SyncEventStatus status, int limit = 100, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            SELECT EventId, AggregateId, AggregateVersion, EventType, PayloadJson, CreatedUtc, RetryCount, NextAttemptUtc, Status, LastError, LastAttemptUtc
            FROM SyncEvents
            WHERE Status = @Status
            ORDER BY CreatedUtc DESC
            LIMIT @Limit;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Status", (int)status);
        cmd.Parameters.AddWithValue("@Limit", limit);

        var events = new List<SyncEvent>();
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            events.Add(ReadEventFromReader(reader));
        }

        return events;
    }

    public async Task CancelEventAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            UPDATE SyncEvents
            SET Status = @CancelledStatus
            WHERE EventId = @EventId AND Status = @PendingStatus;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@CancelledStatus", (int)SyncEventStatus.Cancelled);
        cmd.Parameters.AddWithValue("@EventId", eventId.ToString());
        cmd.Parameters.AddWithValue("@PendingStatus", (int)SyncEventStatus.Pending);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RetryDeadLetterAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        const string sql = @"
            UPDATE SyncEvents
            SET Status = @PendingStatus, RetryCount = 0, NextAttemptUtc = @Now, LastError = NULL
            WHERE EventId = @EventId AND Status = @DeadLetterStatus;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@PendingStatus", (int)SyncEventStatus.Pending);
        cmd.Parameters.AddWithValue("@Now", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@EventId", eventId.ToString());
        cmd.Parameters.AddWithValue("@DeadLetterStatus", (int)SyncEventStatus.DeadLetter);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> PurgeCompletedAsync(TimeSpan retention, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);

        var cutoff = DateTime.UtcNow - retention;

        const string sql = @"
            DELETE FROM SyncEvents
            WHERE Status = @CompletedStatus AND LastAttemptUtc < @Cutoff;";

        using var cmd = new SqliteCommand(sql, connection);
        cmd.Parameters.AddWithValue("@CompletedStatus", (int)SyncEventStatus.Completed);
        cmd.Parameters.AddWithValue("@Cutoff", cutoff.ToString("O"));

        return await cmd.ExecuteNonQueryAsync(ct);
    }

    private static SyncEvent ReadEventFromReader(SqliteDataReader reader)
    {
        return new SyncEvent
        {
            EventId = Guid.Parse(reader.GetString(0)),
            AggregateId = reader.GetString(1),
            AggregateVersion = reader.GetInt64(2),
            EventType = reader.GetString(3),
            PayloadJson = reader.GetString(4),
            CreatedUtc = DateTime.Parse(reader.GetString(5)),
            RetryCount = reader.GetInt32(6),
            NextAttemptUtc = DateTime.Parse(reader.GetString(7)),
            Status = (SyncEventStatus)reader.GetInt32(8),
            LastError = reader.IsDBNull(9) ? null : reader.GetString(9),
            LastAttemptUtc = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10))
        };
    }

    public void Dispose()
    {
        _lock?.Dispose();
    }
}
