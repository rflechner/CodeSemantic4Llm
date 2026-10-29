using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Versioned, regenerable schema for declaration snapshots. It contains no source text.</summary>
internal static class IndexDatabase
{
    private const int SchemaVersion = 1;

    public static SqliteConnection Open(string path, string repositoryRoot, bool writable)
    {
        if (writable)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = writable ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());

        try
        {
            connection.Open();
            ValidateSchema(connection, repositoryRoot, writable);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static SqliteCommand Command(
        SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        }

        return command;
    }

    private static void ValidateSchema(SqliteConnection connection, string root, bool writable)
    {
        using var versionCommand = Command(connection, null, "PRAGMA user_version;");
        var version = Convert.ToInt32(versionCommand.ExecuteScalar());

        if (version == 0 && writable)
        {
            Initialize(connection, root);
        }
        else if (version != SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported index schema version {version}. Use a new index cache directory.");
        }

        using var identity = Command(connection, null, "SELECT root FROM repository_info WHERE id = 1;");

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(identity.ExecuteScalar() as string, root, comparison))
        {
            throw new InvalidOperationException("The index belongs to a different repository root.");
        }
    }

    private static void Initialize(SqliteConnection connection, string root)
    {
        using var tables = Command(connection, null, "SELECT count(*) FROM sqlite_master WHERE type = 'table';");

        if (Convert.ToInt32(tables.ExecuteScalar()) != 0)
        {
            throw new InvalidOperationException("Refusing to initialize an unrecognized database.");
        }

        using var wal = Command(connection, null, "PRAGMA journal_mode = WAL;");
        wal.ExecuteScalar();
        using var transaction = connection.BeginTransaction();
        using var schema = Command(connection, transaction, Schema);
        schema.ExecuteNonQuery();
        using var identity = Command(connection, transaction,
            "INSERT INTO repository_info(id, root) VALUES (1, $root);", ("$root", root));
        identity.ExecuteNonQuery();
        transaction.Commit();
    }

    private const string Schema = """
        CREATE TABLE repository_info(id INTEGER PRIMARY KEY CHECK(id = 1), root TEXT NOT NULL);
        CREATE TABLE snapshots(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            branch TEXT NOT NULL, revision TEXT, configuration_hash TEXT NOT NULL,
            semantic_language TEXT NOT NULL, completed_utc TEXT,
            file_count INTEGER NOT NULL DEFAULT 0, symbol_count INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE active_branches(
            branch TEXT PRIMARY KEY, snapshot_id INTEGER NOT NULL REFERENCES snapshots(id));
        CREATE TABLE indexed_files(
            snapshot_id INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
            path TEXT NOT NULL, content_hash TEXT NOT NULL, analyzer_id TEXT NOT NULL,
            analyzer_version TEXT NOT NULL, symbol_count INTEGER NOT NULL,
            PRIMARY KEY(snapshot_id, path));
        CREATE TABLE symbols(
            snapshot_id INTEGER NOT NULL, path TEXT NOT NULL, ordinal INTEGER NOT NULL,
            language TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL,
            qualified_name TEXT NOT NULL, line INTEGER NOT NULL, column_number INTEGER NOT NULL,
            PRIMARY KEY(snapshot_id, path, ordinal),
            FOREIGN KEY(snapshot_id, path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE);
        CREATE INDEX ix_symbols_name ON symbols(snapshot_id, name, path, line, column_number);
        PRAGMA user_version = 1;
        """;
}
