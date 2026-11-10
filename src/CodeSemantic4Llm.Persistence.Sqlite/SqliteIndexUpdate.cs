using System.Globalization;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Checkpoints complete files for dashboard reads and publishes the verified generation atomically.</summary>
internal sealed class SqliteIndexUpdate : IIndexUpdate
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction _transaction;
    private readonly RepositoryState _state;
    private readonly string _language;
    private readonly long _snapshotId;
    private readonly long? _previousId;
    private readonly string _fingerprint;
    private readonly string? _folderPrefix;
    private readonly SqliteCommand _insertSymbol;
    private readonly SqliteCommand _insertReference;
    private int _filesFromPublishedSnapshot;
    private int _files;
    private int _symbols;
    private bool _published;

    public SqliteIndexUpdate(SqliteConnection connection, RepositoryState state, string fingerprint, string language,
        string? folder)
    {
        _connection = connection;
        _state = state;
        _language = language;
        _transaction = connection.BeginTransaction();
        _previousId = SqliteIndexStore.ReadSnapshot(connection, _transaction, state.Scope)?.SnapshotId;
        _fingerprint = fingerprint;
        _folderPrefix = folder is null ? null : folder.TrimEnd('/') + "/";

        if (_previousId is not null && _folderPrefix is not null)
        {
            using var previous = Command("SELECT configuration_hash FROM snapshots WHERE id = $previous;",
                ("$previous", _previousId));
            if (!string.Equals(previous.ExecuteScalar() as string, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Cannot extend a published index with different indexing settings. " +
                    "Run a complete index after changing the repository YAML or analyzer configuration.");
            }
        }

        using var cleanup = Command("""
            DELETE FROM snapshots WHERE branch = $branch AND completed_utc IS NULL
                AND (configuration_hash <> $hash OR NOT EXISTS
                    (SELECT 1 FROM indexed_files WHERE snapshot_id = snapshots.id));
            """, ("$branch", state.Scope.BranchName), ("$hash", fingerprint));
        cleanup.ExecuteNonQuery();
        using var insert = Command("""
            INSERT INTO snapshots(
                branch, revision, configuration_hash, semantic_language, member_index_version,
                reference_index_version, is_partial)
            VALUES ($branch, $revision, $configuration, $language, 1, $references, $partial) RETURNING id;
            """, ("$branch", state.Scope.BranchName), ("$revision", state.Revision),
            ("$configuration", fingerprint), ("$language", language),
            ("$references", folder is null ? 1 : 0), ("$partial", folder is null ? 0 : 1));
        _snapshotId = Convert.ToInt64(insert.ExecuteScalar());
        _insertSymbol = CreateSymbolInsert();
        _insertReference = CreateReferenceInsert();
    }

    public bool TryReuse(IndexedSourceFile file, CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);

        using var find = Command("""
            SELECT f.snapshot_id, f.symbol_count
            FROM indexed_files f JOIN snapshots s ON s.id = f.snapshot_id
            WHERE s.branch = $branch AND s.id < $snapshot AND (s.completed_utc IS NULL OR s.id = $previous)
              AND s.configuration_hash = $configuration AND s.member_index_version = 1
              AND f.path = $path AND f.content_hash = $hash AND f.analyzer_id = $analyzer
              AND f.analyzer_version = $version AND f.summary_cache_hash = $summaryHash
            ORDER BY s.id DESC LIMIT 1;
            """, ("$branch", _state.Scope.BranchName), ("$snapshot", _snapshotId), ("$previous", _previousId),
            ("$configuration", _fingerprint), ("$path", file.RelativePath), ("$hash", file.ContentHash),
            ("$analyzer", file.AnalyzerId), ("$version", file.AnalyzerVersion), ("$summaryHash", file.SummaryCacheHash));

        long sourceId;
        int symbolCount;
        using (var match = find.ExecuteReader())
        {
            if (!match.Read())
            {
                return false;
            }

            sourceId = match.GetInt64(0);
            symbolCount = match.GetInt32(1);
        }

        using var copy = Command("""
            INSERT INTO indexed_files SELECT $snapshot, path, content_hash, analyzer_id, analyzer_version, symbol_count, summary_cache_hash
            FROM indexed_files WHERE snapshot_id = $previous AND path = $path;
            INSERT INTO symbols SELECT $snapshot, path, ordinal, language, kind, name, qualified_name, line, column_number,
                containing_type_line, containing_type_column, summary
            FROM symbols WHERE snapshot_id = $previous AND path = $path;
            """, ("$snapshot", _snapshotId), ("$previous", sourceId), ("$path", file.RelativePath));
        copy.ExecuteNonQuery();
        _files++;
        _symbols += symbolCount;

        if (sourceId == _previousId)
        {
            _filesFromPublishedSnapshot++;
        }

        CheckpointFile(file.RelativePath);
        return true;
    }

    public bool TryReuseReferences(CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);

        if (_previousId is null || _filesFromPublishedSnapshot != _files)
        {
            return false;
        }

        using var find = Command("""
            SELECT file_count FROM snapshots WHERE id = $previous AND reference_index_version = 1
                AND member_index_version = 1 AND configuration_hash = $configuration;
            """, ("$previous", _previousId), ("$configuration", _fingerprint));

        if (find.ExecuteScalar() is not long previousFileCount || previousFileCount != _files)
        {
            return false;
        }

        using var copy = Command("""
            INSERT INTO symbol_references(
                snapshot_id, target_path, target_line, target_column, usage_path, usage_line, usage_column)
            SELECT $snapshot, target_path, target_line, target_column, usage_path, usage_line, usage_column
            FROM symbol_references WHERE snapshot_id = $previous;
            """, ("$snapshot", _snapshotId), ("$previous", _previousId));
        copy.ExecuteNonQuery();
        return true;
    }

    public void AddReferences(IReadOnlyList<SymbolReference> references, CancellationToken cancellationToken = default,
        Action<int>? onProgress = null)
    {
        EnsureWritable(cancellationToken);
        ArgumentNullException.ThrowIfNull(references);
        var inserted = 0;

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _insertReference.Parameters["$targetPath"].Value = reference.Target.RelativePath;
            _insertReference.Parameters["$targetLine"].Value = reference.Target.Line;
            _insertReference.Parameters["$targetColumn"].Value = reference.Target.Column;
            _insertReference.Parameters["$usagePath"].Value = reference.Usage.RelativePath;
            _insertReference.Parameters["$usageLine"].Value = reference.Usage.Line;
            _insertReference.Parameters["$usageColumn"].Value = reference.Usage.Column;
            _insertReference.ExecuteNonQuery();
            inserted++;

            // Avoid one terminal update per SQLite statement on large repositories.
            if (inserted % 256 == 0 || inserted == references.Count)
            {
                onProgress?.Invoke(inserted);
            }
        }
    }

    public void AddFile(IndexedSourceFile file, IReadOnlyList<SymbolDefinition> symbols, CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);
        using var insert = Command("""
            INSERT INTO indexed_files(snapshot_id, path, content_hash, analyzer_id, analyzer_version, symbol_count, summary_cache_hash)
            VALUES ($snapshot, $path, $hash, $analyzer, $version, $count, $summaryHash);
            """, ("$snapshot", _snapshotId), ("$path", file.RelativePath), ("$hash", file.ContentHash),
            ("$analyzer", file.AnalyzerId), ("$version", file.AnalyzerVersion), ("$count", symbols.Count),
            ("$summaryHash", file.SummaryCacheHash));
        insert.ExecuteNonQuery();

        for (var ordinal = 0; ordinal < symbols.Count; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InsertSymbol(file.RelativePath, ordinal, symbols[ordinal]);
        }

        _files++;
        _symbols += symbols.Count;
        CheckpointFile(file.RelativePath);
    }

    public RepositoryIndexSnapshot Publish(CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);
        if (_folderPrefix is not null)
        {
            MergePreviouslyIndexedFiles(cancellationToken);
        }

        var completed = DateTimeOffset.UtcNow;
        using var publish = Command("""
            UPDATE snapshots SET completed_utc = $completed, file_count = $files, symbol_count = $symbols WHERE id = $snapshot;
            INSERT INTO active_branches(branch, snapshot_id) VALUES ($branch, $snapshot)
            ON CONFLICT(branch) DO UPDATE SET snapshot_id = excluded.snapshot_id;
            DELETE FROM snapshots WHERE id = $previous OR (branch = $branch AND completed_utc IS NULL);
            """, ("$completed", completed.ToString("O", CultureInfo.InvariantCulture)),
            ("$files", _files), ("$symbols", _symbols), ("$snapshot", _snapshotId),
            ("$branch", _state.Scope.BranchName), ("$previous", _previousId));
        publish.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        _transaction.Commit();
        _published = true;

        return new RepositoryIndexSnapshot(_state.Scope, _snapshotId, _state.Revision, _language, completed, _files, _symbols,
            _folderPrefix is not null);
    }

    /// <summary>
    /// Combine this verified folder with previously indexed metadata, without parsing
    /// or calling an LLM for files outside the selected folder. Legacy incomplete
    /// generations remain reusable; the newest matching file version wins.
    /// </summary>
    private void MergePreviouslyIndexedFiles(CancellationToken cancellationToken)
    {
        EnsureWritable(cancellationToken);
        var collation = OperatingSystem.IsWindows() ? "NOCASE" : "BINARY";

        // The file checkpoints may have committed before this final transaction.
        using (var owner = Command("SELECT MAX(id) FROM snapshots WHERE branch = $branch;",
                   ("$branch", _state.Scope.BranchName)))
        {
            if (owner.ExecuteScalar() is not long latest || latest != _snapshotId)
            {
                throw new InvalidOperationException("Another index run superseded this branch update.");
            }
        }

        using (var find = Command($"""
                   CREATE TEMP TABLE retained_files AS
                   SELECT snapshot_id, path FROM (
                       SELECT f.snapshot_id, f.path,
                              ROW_NUMBER() OVER (PARTITION BY f.path ORDER BY f.snapshot_id DESC) AS rank
                       FROM indexed_files f JOIN snapshots s ON s.id = f.snapshot_id
                       WHERE s.branch = $branch AND s.id < $snapshot
                         AND s.configuration_hash = $configuration
                         AND (s.completed_utc IS NULL OR s.id = $previous)
                         AND substr(f.path, 1, length($prefix)) <> $prefix COLLATE {collation}
                         AND NOT EXISTS (
                             SELECT 1 FROM indexed_files current
                             WHERE current.snapshot_id = $snapshot AND current.path = f.path)
                   ) WHERE rank = 1;
                   """, ("$branch", _state.Scope.BranchName), ("$snapshot", _snapshotId),
                   ("$configuration", _fingerprint), ("$previous", _previousId),
                   ("$prefix", _folderPrefix)))
        {
            find.ExecuteNonQuery();
        }

        // The source snapshot id is a temporary join key, never a copy of source.
        using (var copy = Command("""
                   INSERT INTO indexed_files(
                       snapshot_id, path, content_hash, analyzer_id, analyzer_version, symbol_count, summary_cache_hash)
                   SELECT $snapshot, f.path, f.content_hash, f.analyzer_id, f.analyzer_version,
                          f.symbol_count, f.summary_cache_hash
                   FROM retained_files r JOIN indexed_files f
                     ON f.snapshot_id = r.snapshot_id AND f.path = r.path;
                   INSERT INTO symbols(
                       snapshot_id, path, ordinal, language, kind, name, qualified_name, line, column_number,
                       containing_type_line, containing_type_column, summary)
                   SELECT $snapshot, s.path, s.ordinal, s.language, s.kind, s.name, s.qualified_name,
                          s.line, s.column_number, s.containing_type_line, s.containing_type_column, s.summary
                   FROM retained_files r JOIN symbols s
                     ON s.snapshot_id = r.snapshot_id AND s.path = r.path;
                   """, ("$snapshot", _snapshotId)))
        {
            copy.ExecuteNonQuery();
        }

        using (var drop = Command("DROP TABLE retained_files;"))
        {
            drop.ExecuteNonQuery();
        }

        using var counts = Command("""
            SELECT COUNT(*), COALESCE(SUM(symbol_count), 0)
            FROM indexed_files WHERE snapshot_id = $snapshot;
            """, ("$snapshot", _snapshotId));
        using var reader = counts.ExecuteReader();
        if (reader.Read())
        {
            _files = reader.GetInt32(0);
            _symbols = reader.GetInt32(1);
        }
    }

    public void Dispose()
    {
        _insertSymbol.Dispose();
        _insertReference.Dispose();
        _transaction.Dispose();
        _connection.Dispose();
    }

    private void CheckpointFile(string path)
    {
        // Keep one cached copy per unfinished path, including files a shorter retry has yet to visit.
        using var prune = Command("""
            DELETE FROM indexed_files WHERE path = $path AND snapshot_id IN (
                SELECT id FROM snapshots WHERE branch = $branch AND id < $snapshot AND completed_utc IS NULL);
            """, ("$path", path), ("$branch", _state.Scope.BranchName), ("$snapshot", _snapshotId));
        prune.ExecuteNonQuery();

        using var counts = Command("UPDATE snapshots SET file_count = $files, symbol_count = $symbols WHERE id = $snapshot;",
            ("$files", _files), ("$symbols", _symbols), ("$snapshot", _snapshotId));
        counts.ExecuteNonQuery();
        _transaction.Commit();
        _transaction.Dispose();
        _transaction = _connection.BeginTransaction();
        _insertSymbol.Transaction = _transaction;
        _insertReference.Transaction = _transaction;

        // A concurrent writer may have replaced this branch's preview between transactions.
        using var owner = Command("SELECT MAX(id) FROM snapshots WHERE branch = $branch;", ("$branch", _state.Scope.BranchName));
        if (owner.ExecuteScalar() is not long latest || latest != _snapshotId)
        {
            throw new InvalidOperationException("Another index run superseded this branch update.");
        }
    }

    private SqliteCommand CreateSymbolInsert()
    {
        var command = Command("""
            INSERT INTO symbols(snapshot_id, path, ordinal, language, kind, name, qualified_name, line, column_number,
                containing_type_line, containing_type_column, summary)
            VALUES ($snapshot, $path, $ordinal, $language, $kind, $name, $qualified, $line, $column, $parentLine, $parentColumn, $summary);
            """, ("$snapshot", _snapshotId), ("$path", ""), ("$ordinal", 0), ("$language", ""),
            ("$kind", ""), ("$name", ""), ("$qualified", ""), ("$line", 1), ("$column", 1),
            ("$parentLine", null), ("$parentColumn", null), ("$summary", null));
        command.Prepare();
        return command;
    }

    private SqliteCommand CreateReferenceInsert()
    {
        var command = Command("""
            INSERT OR IGNORE INTO symbol_references(
                snapshot_id, target_path, target_line, target_column, usage_path, usage_line, usage_column)
            SELECT $snapshot, $targetPath, $targetLine, $targetColumn, $usagePath, $usageLine, $usageColumn
            WHERE EXISTS (
                SELECT 1 FROM symbols
                WHERE snapshot_id = $snapshot AND path = $targetPath
                    AND line = $targetLine AND column_number = $targetColumn)
            AND EXISTS (
                SELECT 1 FROM indexed_files WHERE snapshot_id = $snapshot AND path = $usagePath);
            """, ("$snapshot", _snapshotId), ("$targetPath", ""), ("$targetLine", 1),
            ("$targetColumn", 1), ("$usagePath", ""), ("$usageLine", 1), ("$usageColumn", 1));
        command.Prepare();
        return command;
    }

    private void InsertSymbol(string path, int ordinal, SymbolDefinition symbol)
    {
        if (symbol.Location.RelativePath != path)
        {
            throw new InvalidOperationException("An analyzer returned a symbol outside the file being indexed.");
        }

        if (symbol.ContainingType is not null && symbol.ContainingType.RelativePath != path)
        {
            throw new InvalidOperationException("An analyzer returned a containing type outside the file being indexed.");
        }

        _insertSymbol.Parameters["$path"].Value = path;
        _insertSymbol.Parameters["$ordinal"].Value = ordinal;
        _insertSymbol.Parameters["$language"].Value = symbol.Language;
        _insertSymbol.Parameters["$kind"].Value = symbol.Kind;
        _insertSymbol.Parameters["$name"].Value = symbol.Name;
        _insertSymbol.Parameters["$qualified"].Value = symbol.QualifiedName;
        _insertSymbol.Parameters["$line"].Value = symbol.Location.Line;
        _insertSymbol.Parameters["$column"].Value = symbol.Location.Column;
        _insertSymbol.Parameters["$parentLine"].Value = (object?)symbol.ContainingType?.Line ?? DBNull.Value;
        _insertSymbol.Parameters["$parentColumn"].Value = (object?)symbol.ContainingType?.Column ?? DBNull.Value;
        _insertSymbol.Parameters["$summary"].Value = (object?)symbol.Summary ?? DBNull.Value;
        _insertSymbol.ExecuteNonQuery();
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters) =>
        IndexDatabase.Command(_connection, _transaction, sql, parameters);

    private void EnsureWritable(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_published)
        {
            throw new InvalidOperationException("This index generation has already been published.");
        }
    }
}
