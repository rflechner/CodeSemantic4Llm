using System.Globalization;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Stores atomic branch snapshots in one SQLite database per checkout, outside the source tree.</summary>
public sealed class SqliteIndexStore(IndexStoragePaths paths) : IIndexStore
{
    public IIndexUpdate BeginUpdate(
        RepositoryState state, string configurationFingerprint, string semanticLanguage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = IndexDatabase.Open(paths.GetDatabasePath(state.Scope.RootPath), state.Scope.RootPath, writable: true);

        try
        {
            return new SqliteIndexUpdate(connection, state, configurationFingerprint, semanticLanguage);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public RepositoryIndexSnapshot? GetSnapshot(RepositoryScope scope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = paths.GetDatabasePath(scope.RootPath);

        if (!File.Exists(path))
        {
            return null;
        }

        using var connection = IndexDatabase.Open(path, scope.RootPath, writable: false);
        return ReadSnapshot(connection, null, scope);
    }

    public SymbolSearchResult FindSymbols(
        RepositoryScope scope, string name, int maxResults, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = paths.GetDatabasePath(scope.RootPath);

        if (!File.Exists(path))
        {
            throw MissingIndex();
        }

        using var connection = IndexDatabase.Open(path, scope.RootPath, writable: false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = ReadSnapshot(connection, transaction, scope) ?? throw MissingIndex();
        using var command = IndexDatabase.Command(connection, transaction, """
            SELECT language, kind, name, qualified_name, path, line, column_number
            FROM symbols WHERE snapshot_id = $snapshot AND name = $name COLLATE BINARY
            ORDER BY path COLLATE BINARY, line, column_number, ordinal LIMIT $limit;
            """, ("$snapshot", snapshot.SnapshotId), ("$name", name), ("$limit", maxResults + 1));
        using var reader = command.ExecuteReader();
        var symbols = new List<SymbolDefinition>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbols.Add(new SymbolDefinition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                new SourceLocation(reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6))));
        }

        var hasMore = symbols.Count > maxResults;
        return new SymbolSearchResult(snapshot, symbols.Take(maxResults).ToArray(), hasMore);
    }

    internal static RepositoryIndexSnapshot? ReadSnapshot(
        SqliteConnection connection, SqliteTransaction? transaction, RepositoryScope scope)
    {
        using var command = IndexDatabase.Command(connection, transaction, """
            SELECT s.id, s.revision, s.semantic_language, s.completed_utc, s.file_count, s.symbol_count
            FROM active_branches b JOIN snapshots s ON s.id = b.snapshot_id WHERE b.branch = $branch;
            """, ("$branch", scope.BranchName));
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? new RepositoryIndexSnapshot(scope, reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetInt32(4), reader.GetInt32(5))
            : null;
    }

    private static InvalidOperationException MissingIndex() =>
        new("No published index exists for this repository and branch. Run the index command first.");
}
