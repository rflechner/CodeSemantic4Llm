using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Versioned, regenerable schema for declaration snapshots. It contains no source text.</summary>
internal static class IndexDatabase
{
    private const int SchemaVersion = 7;

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
        else if (version is not 1 and not 2 and not 3 and not 4 and not 5 and not 6 && version != SchemaVersion)
        {
            throw new InvalidOperationException($"Unsupported index schema version {version}. Use a new index cache directory.");
        }

        using var identity = Command(connection, null, "SELECT root FROM repository_info WHERE id = 1;");

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(identity.ExecuteScalar() as string, root, comparison))
        {
            throw new InvalidOperationException("The index belongs to a different repository root.");
        }

        if (version == 1 && !writable)
        {
            throw new InvalidOperationException("This index needs an upgrade. Run the index command again to record type membership.");
        }

        // Version two is still readable for declaration queries. Reference queries explicitly
        // require a version-three snapshot, and read-only operations never migrate a cache.
        if (writable && version == 1)
        {
            UpgradeFromVersionOne(connection);
        }

        if (writable && version is 1 or 2)
        {
            UpgradeFromVersionTwo(connection);
        }

        if (writable && version is 1 or 2 or 3)
        {
            UpgradeFromVersionThree(connection);
        }

        if (writable && version is 1 or 2 or 3 or 4)
        {
            UpgradeFromVersionFour(connection);
        }

        if (writable && version is >= 1 and <= 5)
        {
            UpgradeFromVersionFive(connection);
        }

        if (writable && version is >= 1 and <= 6)
        {
            UpgradeFromVersionSix(connection);
        }
    }

    private static void UpgradeFromVersionSix(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var columns = Command(connection, transaction, "PRAGMA table_info(snapshots);");
        var hasPartialFlag = false;
        using (var reader = columns.ExecuteReader())
        {
            while (reader.Read())
            {
                hasPartialFlag |= reader.GetString(1) == "is_partial";
            }
        }

        if (!hasPartialFlag)
        {
            using var add = Command(connection, transaction,
                "ALTER TABLE snapshots ADD COLUMN is_partial INTEGER NOT NULL DEFAULT 0;");
            add.ExecuteNonQuery();
        }

        using var version = Command(connection, transaction, "PRAGMA user_version = 7;");
        version.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeFromVersionFive(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var columns = Command(connection, transaction, "PRAGMA table_info(indexed_files);");
        var hasDigest = false;
        using (var reader = columns.ExecuteReader())
        {
            while (reader.Read())
            {
                hasDigest |= reader.GetString(1) == "summary_cache_hash";
            }
        }

        if (!hasDigest)
        {
            using var alter = Command(connection, transaction,
                "ALTER TABLE indexed_files ADD COLUMN summary_cache_hash TEXT NOT NULL DEFAULT '';");
            alter.ExecuteNonQuery();
        }

        using var version = Command(connection, transaction, "PRAGMA user_version = 6;");
        version.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeFromVersionOne(SqliteConnection connection)
    {
        // Preserve existing snapshots; only a subsequent successful index run fills the new metadata.
        using var transaction = connection.BeginTransaction();
        using var migration = Command(connection, transaction, """
            ALTER TABLE symbols ADD COLUMN containing_type_line INTEGER;
            ALTER TABLE symbols ADD COLUMN containing_type_column INTEGER;
            ALTER TABLE snapshots ADD COLUMN member_index_version INTEGER NOT NULL DEFAULT 0;
            CREATE INDEX ix_symbols_container ON symbols(
                snapshot_id, path, containing_type_line, containing_type_column, line, column_number, ordinal);
            PRAGMA user_version = 2;
            """);
        migration.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeFromVersionTwo(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var migration = Command(connection, transaction, """
            ALTER TABLE snapshots ADD COLUMN reference_index_version INTEGER NOT NULL DEFAULT 0;
            CREATE TABLE symbol_references(
                snapshot_id INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
                target_path TEXT NOT NULL, target_line INTEGER NOT NULL, target_column INTEGER NOT NULL,
                usage_path TEXT NOT NULL, usage_line INTEGER NOT NULL, usage_column INTEGER NOT NULL,
                PRIMARY KEY(snapshot_id, target_path, target_line, target_column, usage_path, usage_line, usage_column),
                FOREIGN KEY(snapshot_id, target_path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE,
                FOREIGN KEY(snapshot_id, usage_path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE
            ) WITHOUT ROWID;
            PRAGMA user_version = 3;
            """);
        migration.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeFromVersionThree(SqliteConnection connection)
    {
        // Older real caches lack the column. The test suite also simulates legacy caches
        // by downgrading a newer schema while retaining unrelated columns.
        using var transaction = connection.BeginTransaction();
        using var columns = Command(connection, transaction, "PRAGMA table_info(symbols);");
        var hasSummary = false;

        using (var reader = columns.ExecuteReader())
        {
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "summary", StringComparison.Ordinal))
                {
                    hasSummary = true;
                    break;
                }
            }
        }

        if (!hasSummary)
        {
            using var addSummary = Command(connection, transaction, "ALTER TABLE symbols ADD COLUMN summary TEXT;");
            addSummary.ExecuteNonQuery();
        }

        using var version = Command(connection, transaction, "PRAGMA user_version = 4;");
        version.ExecuteNonQuery();
        transaction.Commit();
    }

    private static void UpgradeFromVersionFour(SqliteConnection connection)
    {
        // The index is derived entirely from symbols, including already-published LLM
        // summaries. Rebuilding it does not call the LLM or read source files.
        using var transaction = connection.BeginTransaction();
        using var schema = Command(connection, transaction, FullTextSchema);
        schema.ExecuteNonQuery();
        using var rebuild = Command(connection, transaction,
            "INSERT INTO symbol_search(symbol_search) VALUES ('rebuild');");
        rebuild.ExecuteNonQuery();
        using var version = Command(connection, transaction, "PRAGMA user_version = 5;");
        version.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Legacy read-only caches have no partial flag; their published indexes are full.</summary>
    public static string PartialColumn(SqliteConnection connection, SqliteTransaction? transaction, string prefix = "")
    {
        using var version = Command(connection, transaction, "PRAGMA user_version;");
        return Convert.ToInt32(version.ExecuteScalar()) >= 7 ? prefix + "is_partial" : "0";
    }

    /// <summary>Legacy read-only caches have no summary column and are never migrated by readers.</summary>
    public static string SummaryColumn(SqliteConnection connection, SqliteTransaction? transaction, string prefix = "")
    {
        using var version = Command(connection, transaction, "PRAGMA user_version;");
        return Convert.ToInt32(version.ExecuteScalar()) >= 4 ? prefix + "summary" : "NULL";
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
        using var fullText = Command(connection, transaction, FullTextSchema);
        fullText.ExecuteNonQuery();
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
            file_count INTEGER NOT NULL DEFAULT 0, symbol_count INTEGER NOT NULL DEFAULT 0,
            member_index_version INTEGER NOT NULL DEFAULT 0,
            reference_index_version INTEGER NOT NULL DEFAULT 0,
            is_partial INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE active_branches(
            branch TEXT PRIMARY KEY, snapshot_id INTEGER NOT NULL REFERENCES snapshots(id));
        CREATE TABLE indexed_files(
            snapshot_id INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
            path TEXT NOT NULL, content_hash TEXT NOT NULL, analyzer_id TEXT NOT NULL,
            analyzer_version TEXT NOT NULL, symbol_count INTEGER NOT NULL,
            summary_cache_hash TEXT NOT NULL DEFAULT '',
            PRIMARY KEY(snapshot_id, path));
        CREATE TABLE symbols(
            snapshot_id INTEGER NOT NULL, path TEXT NOT NULL, ordinal INTEGER NOT NULL,
            language TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL,
            qualified_name TEXT NOT NULL, line INTEGER NOT NULL, column_number INTEGER NOT NULL,
            containing_type_line INTEGER, containing_type_column INTEGER,
            summary TEXT,
            PRIMARY KEY(snapshot_id, path, ordinal),
            FOREIGN KEY(snapshot_id, path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE);
        CREATE TABLE symbol_references(
            snapshot_id INTEGER NOT NULL REFERENCES snapshots(id) ON DELETE CASCADE,
            target_path TEXT NOT NULL, target_line INTEGER NOT NULL, target_column INTEGER NOT NULL,
            usage_path TEXT NOT NULL, usage_line INTEGER NOT NULL, usage_column INTEGER NOT NULL,
            PRIMARY KEY(snapshot_id, target_path, target_line, target_column, usage_path, usage_line, usage_column),
            FOREIGN KEY(snapshot_id, target_path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE,
            FOREIGN KEY(snapshot_id, usage_path) REFERENCES indexed_files(snapshot_id, path) ON DELETE CASCADE
        ) WITHOUT ROWID;
        CREATE INDEX ix_symbols_name ON symbols(snapshot_id, name, path, line, column_number);
        CREATE INDEX ix_symbols_container ON symbols(
            snapshot_id, path, containing_type_line, containing_type_column, line, column_number, ordinal);
        PRAGMA user_version = 7;
        """;

    // External-content FTS keeps the authoritative name/summary text solely in
    // symbols. SQLite maintains the term index in the same transaction as symbol
    // inserts, copies, updates and cascading deletes across snapshot publication.
    private const string FullTextSchema = """
        CREATE VIRTUAL TABLE IF NOT EXISTS symbol_search USING fts5(
            name, qualified_name, summary,
            content='symbols', content_rowid='rowid',
            tokenize='unicode61 remove_diacritics 2',
            prefix='2 3 4'
        );
        CREATE TRIGGER IF NOT EXISTS symbol_search_insert AFTER INSERT ON symbols BEGIN
            INSERT INTO symbol_search(rowid, name, qualified_name, summary)
            VALUES (new.rowid, new.name, new.qualified_name, new.summary);
        END;
        CREATE TRIGGER IF NOT EXISTS symbol_search_delete AFTER DELETE ON symbols BEGIN
            INSERT INTO symbol_search(symbol_search, rowid, name, qualified_name, summary)
            VALUES ('delete', old.rowid, old.name, old.qualified_name, old.summary);
        END;
        CREATE TRIGGER IF NOT EXISTS symbol_search_update AFTER UPDATE ON symbols BEGIN
            INSERT INTO symbol_search(symbol_search, rowid, name, qualified_name, summary)
            VALUES ('delete', old.rowid, old.name, old.qualified_name, old.summary);
            INSERT INTO symbol_search(rowid, name, qualified_name, summary)
            VALUES (new.rowid, new.name, new.qualified_name, new.summary);
        END;
        """;
}
