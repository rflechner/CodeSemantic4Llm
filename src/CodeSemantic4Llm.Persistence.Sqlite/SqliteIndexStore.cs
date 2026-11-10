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
        CancellationToken cancellationToken = default, string? folder = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = IndexDatabase.Open(paths.GetDatabasePath(state.Scope.RootPath), state.Scope.RootPath, writable: true);

        try
        {
            return new SqliteIndexUpdate(connection, state, configurationFingerprint, semanticLanguage, folder);
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
        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction);
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT language, kind, name, qualified_name, path, line, column_number, containing_type_line, containing_type_column, {summaryColumn}
            FROM symbols WHERE snapshot_id = $snapshot AND name = $name COLLATE BINARY
            ORDER BY path COLLATE BINARY, line, column_number, ordinal LIMIT $limit;
            """, ("$snapshot", snapshot.SnapshotId), ("$name", name), ("$limit", maxResults + 1));
        using var reader = command.ExecuteReader();
        var symbols = new List<SymbolDefinition>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbols.Add(ReadSymbol(reader));
        }

        var hasMore = symbols.Count > maxResults;
        return new SymbolSearchResult(snapshot, symbols.Take(maxResults).ToArray(), hasMore);
    }

    /// <inheritdoc />
    public SymbolSearchResult SearchCode(
        RepositoryScope scope, string query, string? kind, int maxResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var terms = FullTextSearchQuery.Terms(query, maxResults);
        var path = paths.GetDatabasePath(scope.RootPath);

        if (!File.Exists(path))
        {
            throw MissingIndex();
        }

        using var connection = IndexDatabase.Open(path, scope.RootPath, writable: false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = ReadSnapshot(connection, transaction, scope) ?? throw MissingIndex();

        var symbols = ReadSearchMatches(connection, transaction, snapshot.SnapshotId, terms, kind, maxResults, cancellationToken);
        return new SymbolSearchResult(snapshot, symbols.Take(maxResults).ToArray(), symbols.Count > maxResults);
    }

    internal static List<SymbolDefinition> ReadSearchMatches(
        SqliteConnection connection, SqliteTransaction transaction, long snapshotId,
        IReadOnlyList<string> terms, string? kind, int maxResults, CancellationToken cancellationToken)
    {
        using (var version = IndexDatabase.Command(connection, transaction, "PRAGMA user_version;"))
        {
            if (Convert.ToInt32(version.ExecuteScalar()) < 8)
            {
                throw new InvalidOperationException(
                    "This index needs upgraded full-text search metadata. Run the index command again.");
            }
        }

        var match = string.Join(" OR ", terms);

        // Each membership check uses the inverted index, never a substring scan of symbols.
        var coverage = string.Join(" + ", terms.Select((_, index) =>
            $"(s.rowid IN (SELECT rowid FROM symbol_search WHERE symbol_search MATCH $term{index}))"));
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT s.language, s.kind, s.name, s.qualified_name, s.path,
                   s.line, s.column_number, s.containing_type_line, s.containing_type_column, s.summary
            FROM symbol_search
            JOIN symbols s ON s.rowid = symbol_search.rowid
            WHERE symbol_search MATCH $match
              AND s.snapshot_id = $snapshot
              AND ($kind IS NULL OR s.kind = $kind COLLATE BINARY)
            ORDER BY ({coverage}) DESC,
                     CASE WHEN s.kind IN ('class', 'interface', 'struct', 'record', 'enum', 'delegate') THEN 0 ELSE 1 END,
                     bm25(symbol_search, 8.0, 4.0, 1.0, 8.0),
                     s.path COLLATE BINARY, s.line, s.column_number, s.ordinal
            LIMIT $limit;
            """,
            ("$match", match), ("$snapshot", snapshotId),
            ("$kind", string.IsNullOrWhiteSpace(kind) ? null : kind.Trim()),
            ("$limit", maxResults + 1));

        for (var index = 0; index < terms.Count; index++)
        {
            command.Parameters.AddWithValue($"$term{index}", terms[index]);
        }

        using var reader = command.ExecuteReader();
        var symbols = new List<SymbolDefinition>(maxResults + 1);

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            symbols.Add(ReadSymbol(reader));
        }

        return symbols;
    }

    public IReadOnlyList<IndexedDeclaration> SelectDeclarations(
        RepositoryScope scope, DeclarationSelector selector, bool typesOnly,
        CancellationToken cancellationToken = default)
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
        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction, "s.");
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT s.language, s.kind, s.name, s.qualified_name, s.path, s.line, s.column_number,
                   s.containing_type_line, s.containing_type_column, {summaryColumn}, f.content_hash
            FROM symbols s JOIN indexed_files f ON f.snapshot_id = s.snapshot_id AND f.path = s.path
            WHERE s.snapshot_id = $snapshot AND s.path = $path
                AND (s.name = $name COLLATE BINARY OR s.qualified_name = $name COLLATE BINARY
                     OR substr(s.qualified_name, -length($qualifiedSuffix)) = $qualifiedSuffix COLLATE BINARY)
                AND ($line IS NULL OR s.line = $line) AND ($column IS NULL OR s.column_number = $column)
                AND (s.kind IN ('class', 'struct', 'interface', 'record', 'enum', 'delegate')
                     OR ($typesOnly = 0 AND s.kind = 'method'))
            ORDER BY s.line, s.column_number, s.ordinal LIMIT 2;
            """, ("$snapshot", snapshot.SnapshotId), ("$path", selector.RelativePath), ("$name", selector.Name),
            ("$qualifiedSuffix", selector.QualifiedNameSuffix),
            ("$line", selector.Line), ("$column", selector.Column), ("$typesOnly", typesOnly ? 1 : 0));
        using var reader = command.ExecuteReader();
        var declarations = new List<IndexedDeclaration>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            declarations.Add(new IndexedDeclaration(snapshot, ReadSymbol(reader), reader.GetString(10)));
        }

        return declarations;
    }

    /// <inheritdoc />
    public IReadOnlyList<TypeUsagePreview> GetTypeUsagePreviews(
        RepositoryScope scope, long snapshotId, IReadOnlyList<SourceLocation> typeLocations,
        int maxUsagesPerType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeLocations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUsagesPerType);

        if (maxUsagesPerType > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUsagesPerType), "Limit must not exceed 200.");
        }

        if (typeLocations.Count == 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var path = paths.GetDatabasePath(scope.RootPath);

        if (!File.Exists(path))
        {
            throw MissingIndex();
        }

        using var connection = IndexDatabase.Open(path, scope.RootPath, writable: false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = ReadSnapshot(connection, transaction, scope) ?? throw MissingIndex();

        if (snapshot.SnapshotId != snapshotId)
        {
            throw new InvalidOperationException("The active index snapshot changed. Search again before reading type usages.");
        }

        // Folder-only and legacy snapshots can expose declarations without a complete
        // reference index. Do not misrepresent that state as zero references.
        bool isAvailable;
        using (var version = IndexDatabase.Command(connection, transaction, "PRAGMA user_version;"))
        {
            isAvailable = Convert.ToInt32(version.ExecuteScalar()) >= 3;
        }

        if (isAvailable)
        {
            using var version = IndexDatabase.Command(connection, transaction,
                "SELECT reference_index_version FROM snapshots WHERE id = $snapshot;",
                ("$snapshot", snapshotId));
            isAvailable = Convert.ToInt32(version.ExecuteScalar()) == 1;
        }

        if (!isAvailable)
        {
            return typeLocations.Select(location =>
                new TypeUsagePreview(location, [], HasMore: false, IsAvailable: false)).ToArray();
        }

        // Reuse one connection and prepared query for all returned types, including searches
        // with many matches. Each sample reads just one extra row to establish HasMore.
        using var command = IndexDatabase.Command(connection, transaction, """
            SELECT usage_path, usage_line, usage_column
            FROM symbol_references
            WHERE snapshot_id = $snapshot AND target_path = $path
                AND target_line = $line AND target_column = $column
            ORDER BY usage_path COLLATE BINARY, usage_line, usage_column
            LIMIT $limit;
            """, ("$snapshot", snapshotId), ("$path", ""),
            ("$line", 0), ("$column", 0), ("$limit", maxUsagesPerType + 1));

        var previews = new List<TypeUsagePreview>(typeLocations.Count);

        foreach (var location in typeLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            command.Parameters["$path"].Value = location.RelativePath;
            command.Parameters["$line"].Value = location.Line;
            command.Parameters["$column"].Value = location.Column;

            var usages = new List<SourceLocation>(maxUsagesPerType + 1);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    usages.Add(new SourceLocation(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
                }
            }

            previews.Add(new TypeUsagePreview(
                location, usages.Take(maxUsagesPerType).ToArray(),
                HasMore: usages.Count > maxUsagesPerType, IsAvailable: true));
        }

        return previews;
    }

    public SymbolUsagePage FindUsages(
        RepositoryScope scope, SourceLocation targetLocation, long snapshotId,
        int maxResults, int offset, CancellationToken cancellationToken = default)
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

        if (snapshot.SnapshotId != snapshotId)
        {
            throw new InvalidOperationException("The active index snapshot changed. Call find_symbols again before finding usages.");
        }

        using (var version = IndexDatabase.Command(connection, transaction, "PRAGMA user_version;"))
        {
            if (Convert.ToInt32(version.ExecuteScalar()) < 3)
            {
                throw new InvalidOperationException("This branch snapshot has no reference metadata. Run the index command again.");
            }
        }

        using (var version = IndexDatabase.Command(connection, transaction,
                   "SELECT reference_index_version FROM snapshots WHERE id = $snapshot;", ("$snapshot", snapshotId)))
        {
            if (Convert.ToInt32(version.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException("This branch snapshot has no reference metadata. Run the index command again.");
            }
        }

        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction);
        using var targetCommand = IndexDatabase.Command(connection, transaction, $"""
            SELECT language, kind, name, qualified_name, path, line, column_number, containing_type_line, containing_type_column, {summaryColumn}
            FROM symbols WHERE snapshot_id = $snapshot AND path = $path AND line = $line AND column_number = $column
                AND kind IN ('method', 'class', 'struct', 'interface', 'record', 'enum', 'delegate');
            """, ("$snapshot", snapshotId), ("$path", targetLocation.RelativePath),
            ("$line", targetLocation.Line), ("$column", targetLocation.Column));
        SymbolDefinition target;
        using (var reader = targetCommand.ExecuteReader())
        {
            target = reader.Read() ? ReadSymbol(reader)
                : throw new InvalidOperationException("No indexed method or type exists at this location. Use the location from find_symbols.");
        }

        using var command = IndexDatabase.Command(connection, transaction, """
            SELECT usage_path, usage_line, usage_column
            FROM symbol_references
            WHERE snapshot_id = $snapshot AND target_path = $path AND target_line = $line AND target_column = $column
            ORDER BY usage_path COLLATE BINARY, usage_line, usage_column
            LIMIT $limit OFFSET $offset;
            """, ("$snapshot", snapshotId), ("$path", targetLocation.RelativePath),
            ("$line", targetLocation.Line), ("$column", targetLocation.Column),
            ("$limit", maxResults + 1), ("$offset", offset));

        var usages = new List<SourceLocation>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                usages.Add(new SourceLocation(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
            }
        }

        int? nextOffset = usages.Count > maxResults ? checked(offset + maxResults) : null;
        return new SymbolUsagePage(snapshot, target, usages.Take(maxResults).ToArray(), nextOffset);
    }

    public IndexedMethod GetIndexedMethod(
        RepositoryScope scope, SourceLocation methodLocation, long snapshotId,
        CancellationToken cancellationToken = default)
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

        if (snapshot.SnapshotId != snapshotId)
        {
            throw new InvalidOperationException(
                "The active index snapshot changed. Call find_symbols again before editing a method.");
        }

        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction, "s.");
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT s.language, s.kind, s.name, s.qualified_name, s.path, s.line, s.column_number,
                   s.containing_type_line, s.containing_type_column, {summaryColumn}, f.content_hash
            FROM symbols s
            JOIN indexed_files f ON f.snapshot_id = s.snapshot_id AND f.path = s.path
            WHERE s.snapshot_id = $snapshot AND s.path = $path
                AND s.line = $line AND s.column_number = $column AND s.kind = 'method';
            """, ("$snapshot", snapshotId), ("$path", methodLocation.RelativePath),
            ("$line", methodLocation.Line), ("$column", methodLocation.Column));

        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new IndexedMethod(snapshot, ReadSymbol(reader), reader.GetString(10))
            : throw new InvalidOperationException(
                "No indexed method exists at this declaration location. Use find_symbols with kind 'method'.");
    }

    public TypeMemberPage GetTypeMembers(
        RepositoryScope scope, SourceLocation typeLocation, long snapshotId,
        string? kind, int maxResults, int offset, CancellationToken cancellationToken = default)
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
        ValidateMemberSnapshot(connection, transaction, snapshot, snapshotId);
        var type = ReadType(connection, transaction, snapshotId, typeLocation);
        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction);
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT language, kind, name, qualified_name, path, line, column_number, containing_type_line, containing_type_column, {summaryColumn}
            FROM symbols WHERE snapshot_id = $snapshot AND path = $path
                AND containing_type_line = $line AND containing_type_column = $column
                AND ($kind IS NULL OR kind = $kind COLLATE BINARY)
            ORDER BY line, column_number, ordinal LIMIT $limit OFFSET $offset;
            """, ("$snapshot", snapshotId), ("$path", typeLocation.RelativePath), ("$line", typeLocation.Line),
            ("$column", typeLocation.Column), ("$kind", kind), ("$limit", maxResults + 1), ("$offset", offset));
        using var reader = command.ExecuteReader();
        var members = new List<SymbolDefinition>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            members.Add(ReadSymbol(reader));
        }

        int? nextOffset = members.Count > maxResults ? checked(offset + maxResults) : null;
        return new TypeMemberPage(snapshot, type, members.Take(maxResults).ToArray(), nextOffset);
    }

    private static void ValidateMemberSnapshot(
        SqliteConnection connection, SqliteTransaction transaction, RepositoryIndexSnapshot snapshot, long expectedId)
    {
        if (snapshot.SnapshotId != expectedId)
        {
            throw new InvalidOperationException("The active index snapshot changed. Call find_symbols again before listing members.");
        }

        using var command = IndexDatabase.Command(connection, transaction,
            "SELECT member_index_version FROM snapshots WHERE id = $snapshot;", ("$snapshot", expectedId));

        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException("This branch snapshot has no type membership metadata. Run the index command again.");
        }
    }

    private static SymbolDefinition ReadType(
        SqliteConnection connection, SqliteTransaction transaction, long snapshotId, SourceLocation location)
    {
        var summaryColumn = IndexDatabase.SummaryColumn(connection, transaction);
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT language, kind, name, qualified_name, path, line, column_number, containing_type_line, containing_type_column, {summaryColumn}
            FROM symbols WHERE snapshot_id = $snapshot AND path = $path AND line = $line AND column_number = $column
                AND kind IN ('class', 'struct', 'interface', 'record', 'enum');
            """, ("$snapshot", snapshotId), ("$path", location.RelativePath), ("$line", location.Line), ("$column", location.Column));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSymbol(reader)
            : throw new InvalidOperationException("No type declaration exists at this indexed location. Use the type's location from find_symbols.");
    }

    private static SymbolDefinition ReadSymbol(SqliteDataReader reader)
    {
        var path = reader.GetString(4);
        var containingType = reader.IsDBNull(7) ? null : new SourceLocation(path, reader.GetInt32(7), reader.GetInt32(8));
        return new SymbolDefinition(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            new SourceLocation(path, reader.GetInt32(5), reader.GetInt32(6)), containingType,
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    internal static RepositoryIndexSnapshot? ReadSnapshot(
        SqliteConnection connection, SqliteTransaction? transaction, RepositoryScope scope)
    {
        var partialColumn = IndexDatabase.PartialColumn(connection, transaction, "s.");
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT s.id, s.revision, s.semantic_language, s.completed_utc, s.file_count, s.symbol_count,
                   {partialColumn}
            FROM active_branches b JOIN snapshots s ON s.id = b.snapshot_id WHERE b.branch = $branch;
            """, ("$branch", scope.BranchName));
        using var reader = command.ExecuteReader();

        return reader.Read()
            ? new RepositoryIndexSnapshot(scope, reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2), DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6) != 0)
            : null;
    }

    private static InvalidOperationException MissingIndex() =>
        new("No published index exists for this repository and branch. Run the index command first.");
}
