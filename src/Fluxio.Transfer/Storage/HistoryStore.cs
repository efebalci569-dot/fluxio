using System.Globalization;
using Fluxio.Transfer.Transfers;
using Microsoft.Data.Sqlite;

namespace Fluxio.Transfer.Storage;

public enum HistoryOutcome
{
    Completed,
    Failed,
    Cancelled,
    Declined,
    Skipped,
}

/// <summary>
/// One finished file. Deliberately minimal: no paths, no hashes, no content; only what the
/// History page shows.
/// </summary>
public sealed record HistoryEntry(
    long Id,
    DateTimeOffset Timestamp,
    TransferDirection Direction,
    string FileName,
    long Size,
    HistoryOutcome Outcome,
    string DeviceLabel,
    bool Verified);

public interface IHistorySink
{
    void Record(HistoryEntry entry);
}

/// <summary>Transfer history in a local SQLite database (%LOCALAPPDATA%\Fluxio\history.db).</summary>
public sealed class HistoryStore : IHistorySink
{
    private readonly string _connectionString;
    private readonly object _gate = new();

    public HistoryStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp  TEXT    NOT NULL,
                direction  INTEGER NOT NULL,
                file_name  TEXT    NOT NULL,
                size       INTEGER NOT NULL,
                outcome    INTEGER NOT NULL,
                device     TEXT    NOT NULL,
                verified   INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_history_timestamp ON history (timestamp DESC);
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>Raised (on the recording thread) after the history changed.</summary>
    public event EventHandler? Changed;

    public void Record(HistoryEntry entry)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO history (timestamp, direction, file_name, size, outcome, device, verified)
                VALUES ($ts, $dir, $name, $size, $outcome, $device, $verified);
                """;
            command.Parameters.AddWithValue("$ts", entry.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$dir", (int)entry.Direction);
            command.Parameters.AddWithValue("$name", entry.FileName);
            command.Parameters.AddWithValue("$size", entry.Size);
            command.Parameters.AddWithValue("$outcome", (int)entry.Outcome);
            command.Parameters.AddWithValue("$device", entry.DeviceLabel);
            command.Parameters.AddWithValue("$verified", entry.Verified ? 1 : 0);
            command.ExecuteNonQuery();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<HistoryEntry> Query(int limit = 500)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, timestamp, direction, file_name, size, outcome, device, verified
                FROM history ORDER BY timestamp DESC, id DESC LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            var result = new List<HistoryEntry>();
            while (reader.Read())
            {
                result.Add(new HistoryEntry(
                    reader.GetInt64(0),
                    DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
                    (TransferDirection)reader.GetInt32(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    (HistoryOutcome)reader.GetInt32(5),
                    reader.GetString(6),
                    reader.GetInt32(7) != 0));
            }

            return result;
        }
    }

    public void Delete(IEnumerable<long> ids)
    {
        lock (_gate)
        {
            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM history WHERE id = $id;";
            var parameter = command.Parameters.Add("$id", SqliteType.Integer);
            foreach (var id in ids)
            {
                parameter.Value = id;
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            using var connection = Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM history; VACUUM;";
            command.ExecuteNonQuery();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
