using Compressi.Core.Models;
using Microsoft.Data.Sqlite;

namespace Compressi.Core.Services;

public sealed class HistoryStore
{
    private readonly string _connectionString;
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public HistoryStore(string? databasePath = null)
    {
        var path = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Compressi",
            "history.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = $"Data Source={path}";
    }

    public IReadOnlyList<HistoryEntry> GetAll()
    {
        var entries = new List<HistoryEntry>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_name, source_path, output_path, preset, format, status,
                   original_size, compressed_size, ratio, created_at
            FROM history
            ORDER BY created_at DESC;
            """;

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public IReadOnlyList<HistoryEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetAll();
        }

        var entries = new List<HistoryEntry>();
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, source_name, source_path, output_path, preset, format, status,
                   original_size, compressed_size, ratio, created_at
            FROM history
            WHERE source_name LIKE $query
            ORDER BY created_at DESC;
            """;
        command.Parameters.AddWithValue("$query", $"%{query}%");

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(ReadEntry(reader));
        }

        return entries;
    }

    public long Add(HistoryEntry entry)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history
            (source_name, source_path, output_path, preset, format, status,
             original_size, compressed_size, ratio, created_at)
            VALUES ($source_name, $source_path, $output_path, $preset, $format, $status,
                    $original_size, $compressed_size, $ratio, $created_at);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$source_name", entry.SourceName);
        command.Parameters.AddWithValue("$source_path", entry.SourcePath);
        command.Parameters.AddWithValue("$output_path", entry.OutputPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$preset", entry.Preset.ToString());
        command.Parameters.AddWithValue("$format", entry.Format.ToString());
        command.Parameters.AddWithValue("$status", entry.Status.ToString());
        command.Parameters.AddWithValue("$original_size", entry.OriginalSizeBytes);
        command.Parameters.AddWithValue("$compressed_size", entry.CompressedSizeBytes);
        command.Parameters.AddWithValue("$ratio", entry.CompressionRatioPercent);
        command.Parameters.AddWithValue("$created_at", entry.CreatedAt.UtcDateTime.ToString("O"));

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Delete(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private void EnsureSchema(SqliteConnection connection)
    {
        if (_schemaReady)
        {
            return;
        }

        lock (_schemaGate)
        {
            if (_schemaReady)
            {
                return;
            }

            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_name TEXT NOT NULL,
                    source_path TEXT NOT NULL,
                    output_path TEXT,
                    preset TEXT NOT NULL,
                    format TEXT NOT NULL,
                    status TEXT NOT NULL,
                    original_size INTEGER NOT NULL,
                    compressed_size INTEGER NOT NULL,
                    ratio REAL NOT NULL,
                    created_at TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            _schemaReady = true;
        }
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        EnsureSchema(connection);
        return connection;
    }

    private static HistoryEntry ReadEntry(SqliteDataReader reader)
    {
        return HistoryEntry.Create(
            id: reader.GetInt64(0),
            sourceName: reader.GetString(1),
            sourcePath: reader.GetString(2),
            outputPath: reader.IsDBNull(3) ? null : reader.GetString(3),
            preset: Enum.Parse<CompressionPreset>(reader.GetString(4)),
            format: Enum.Parse<OutputFormat>(reader.GetString(5)),
            status: Enum.Parse<CompressionJobStatus>(reader.GetString(6)),
            originalSizeBytes: reader.GetInt64(7),
            compressedSizeBytes: reader.GetInt64(8),
            compressionRatioPercent: reader.GetDouble(9),
            createdAt: DateTimeOffset.Parse(reader.GetString(10)));
    }
}
