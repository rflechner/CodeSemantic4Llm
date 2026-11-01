using System.Globalization;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>A single writer transaction publishes a complete snapshot; readers retain the previous WAL snapshot.</summary>
internal sealed class SqliteIndexUpdate : IIndexUpdate
{
    private readonly SqliteConnection _connection;
    private readonly SqliteTransaction _transaction;
    private readonly RepositoryState _state;
    private readonly string _language;
    private readonly long _snapshotId;
    private readonly long? _previousId;
    private readonly long? _reusableId;
    private readonly SqliteCommand _insertSymbol;
    private readonly SqliteCommand _insertReference;
    private int _reusedFiles;
    private int _files;
    private int _symbols;
    private bool _published;

    public SqliteIndexUpdate(SqliteConnection connection, RepositoryState state, string fingerprint, string language)
    {
        _connection = connection;
        _state = state;
        _language = language;
        _transaction = connection.BeginTransaction();
        _previousId = SqliteIndexStore.ReadSnapshot(connection, _transaction, state.Scope)?.SnapshotId;
        _reusableId = GetReusableSnapshot(fingerprint);
        using var insert = Command("""
            INSERT INTO snapshots(branch, revision, configuration_hash, semantic_language, member_index_version, reference_index_version)
            VALUES ($branch, $revision, $configuration, $language, 1, 1) RETURNING id;
            """, ("$branch", state.Scope.BranchName), ("$revision", state.Revision),
            ("$configuration", fingerprint), ("$language", language));
        _snapshotId = Convert.ToInt64(insert.ExecuteScalar());
        _insertSymbol = CreateSymbolInsert();
        _insertReference = CreateReferenceInsert();
    }

    public bool TryReuse(IndexedSourceFile file, CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);

        if (_reusableId is null)
        {
            return false;
        }

        using var find = Command("""
            SELECT symbol_count FROM indexed_files WHERE snapshot_id = $previous AND path = $path
            AND content_hash = $hash AND analyzer_id = $analyzer AND analyzer_version = $version
            AND summary_cache_hash = $summaryHash;
            """, ("$previous", _reusableId), ("$path", file.RelativePath), ("$hash", file.ContentHash),
            ("$analyzer", file.AnalyzerId), ("$version", file.AnalyzerVersion), ("$summaryHash", file.SummaryCacheHash));
        var symbolCount = find.ExecuteScalar();

        if (symbolCount is null)
        {
            return false;
        }

        using var copy = Command("""
            INSERT INTO indexed_files SELECT $snapshot, path, content_hash, analyzer_id, analyzer_version, symbol_count, summary_cache_hash
            FROM indexed_files WHERE snapshot_id = $previous AND path = $path;
            INSERT INTO symbols SELECT $snapshot, path, ordinal, language, kind, name, qualified_name, line, column_number,
                containing_type_line, containing_type_column, summary
            FROM symbols WHERE snapshot_id = $previous AND path = $path;
            """, ("$snapshot", _snapshotId), ("$previous", _reusableId), ("$path", file.RelativePath));
        copy.ExecuteNonQuery();
        _files++;
        _reusedFiles++;
        _symbols += Convert.ToInt32(symbolCount);
        return true;
    }

    public bool TryReuseReferences(CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);

        if (_reusableId is null || _reusedFiles != _files)
        {
            return false;
        }

        using var find = Command("""
            SELECT file_count FROM snapshots WHERE id = $previous AND reference_index_version = 1;
            """, ("$previous", _reusableId));

        if (find.ExecuteScalar() is not long previousFileCount || previousFileCount != _files)
        {
            return false;
        }

        using var copy = Command("""
            INSERT INTO symbol_references(
                snapshot_id, target_path, target_line, target_column, usage_path, usage_line, usage_column)
            SELECT $snapshot, target_path, target_line, target_column, usage_path, usage_line, usage_column
            FROM symbol_references WHERE snapshot_id = $previous;
            """, ("$snapshot", _snapshotId), ("$previous", _reusableId));
        copy.ExecuteNonQuery();
        return true;
    }

    public void AddReferences(IReadOnlyList<SymbolReference> references, CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);
        ArgumentNullException.ThrowIfNull(references);

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
    }

    public RepositoryIndexSnapshot Publish(CancellationToken cancellationToken = default)
    {
        EnsureWritable(cancellationToken);
        var completed = DateTimeOffset.UtcNow;
        using var publish = Command("""
            UPDATE snapshots SET completed_utc = $completed, file_count = $files, symbol_count = $symbols WHERE id = $snapshot;
            INSERT INTO active_branches(branch, snapshot_id) VALUES ($branch, $snapshot)
            ON CONFLICT(branch) DO UPDATE SET snapshot_id = excluded.snapshot_id;
            DELETE FROM snapshots WHERE id = $previous;
            """, ("$completed", completed.ToString("O", CultureInfo.InvariantCulture)),
            ("$files", _files), ("$symbols", _symbols), ("$snapshot", _snapshotId),
            ("$branch", _state.Scope.BranchName), ("$previous", _previousId));
        publish.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested();
        _transaction.Commit();
        _published = true;

        return new RepositoryIndexSnapshot(_state.Scope, _snapshotId, _state.Revision, _language, completed, _files, _symbols);
    }

    public void Dispose()
    {
        _insertSymbol.Dispose();
        _insertReference.Dispose();
        _transaction.Dispose();
        _connection.Dispose();
    }

    private long? GetReusableSnapshot(string fingerprint)
    {
        using var command = Command("SELECT id FROM snapshots WHERE id = $id AND configuration_hash = $hash AND member_index_version = 1;",
            ("$id", _previousId), ("$hash", fingerprint));
        return command.ExecuteScalar() is long id ? id : null;
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
