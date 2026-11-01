using System.Globalization;
using System.Text.RegularExpressions;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>
/// Browses every published branch in the shared cache using SQLite read-only connections.
/// It does not enumerate source trees, run analyzers, migrate schemas or create databases.
/// </summary>
public sealed class SqliteIndexDashboardReader(IndexStoragePaths paths) : IIndexDashboardReader
{
    private static readonly Regex CacheIdPattern = new(
        "^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<DashboardProject> ListProjects(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.Root))
        {
            return [];
        }

        var projects = new List<DashboardProject>();

        foreach (var directory in Directory.EnumerateDirectories(paths.Root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = Path.GetFileName(directory);

            if (!CacheIdPattern.IsMatch(id))
            {
                continue;
            }

            // Individual caches may be obsolete or damaged. Keep other projects discoverable.
            try
            {
                using var connection = OpenProject(id);
                var root = ReadRoot(connection);
                var version = ReadSchemaVersion(connection);
                var branches = ReadBranches(connection, version, cancellationToken);
                var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(root));
                projects.Add(new DashboardProject(id, string.IsNullOrEmpty(name) ? root : name, root, branches));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or SqliteException or InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                // A broken cache must not make unrelated published indexes inaccessible.
            }
        }

        return projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(project => project.RootPath, StringComparer.Ordinal)
            .ToArray();
    }

    public DashboardSummary GetSummary(
        string projectId, string branch, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = OpenProject(projectId);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshotId = RequireSnapshot(connection, transaction, branch);
        var version = ReadSchemaVersion(connection, transaction);

        var kinds = ReadFacets(connection, transaction, snapshotId, "kind", cancellationToken);
        var languages = ReadFacets(connection, transaction, snapshotId, "language", cancellationToken);

        long references = 0;

        if (version >= 3)
        {
            using var command = IndexDatabase.Command(connection, transaction,
                "SELECT COUNT(*) FROM symbol_references WHERE snapshot_id = $snapshot;",
                ("$snapshot", snapshotId));
            references = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        return new DashboardSummary(references, kinds, languages);
    }

    public DashboardCodeSearchResult SearchCode(
        string projectId, string branch, string query, string? kind, int maxResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RepositoryScope scope;

        using (var connection = OpenProject(projectId))
        {
            scope = new RepositoryScope(ReadRoot(connection), branch);
            using var transaction = connection.BeginTransaction(deferred: true);
            RequireSnapshot(connection, transaction, branch);
        }

        // Resolve only validated cache ids, then reuse the MCP store's query and ordering.
        // The dashboard can inspect a saved branch even when its checkout is unavailable.
        var result = new SqliteIndexStore(paths).SearchCode(scope, query, kind, maxResults, cancellationToken);
        var symbols = result.Symbols.Select(symbol => new DashboardSymbol(
            symbol.Language, symbol.Kind, symbol.Name, symbol.QualifiedName,
            symbol.Location.RelativePath, symbol.Location.Line, symbol.Location.Column,
            symbol.ContainingType?.Line, symbol.ContainingType?.Column, symbol.Summary)).ToArray();

        return new DashboardCodeSearchResult(result.Snapshot, symbols, result.HasMore);
    }

    public DashboardPage<DashboardFile> GetFiles(
        string projectId, string branch, string? search, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);
        using var connection = OpenProject(projectId);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshotId = RequireSnapshot(connection, transaction, branch);
        var parameters = new (string Name, object? Value)[]
        {
            ("$snapshot", snapshotId), ("$search", EmptyToNull(search))
        };

        const string from = """
            FROM indexed_files f
            WHERE f.snapshot_id = $snapshot
              AND ($search IS NULL OR instr(lower(f.path), lower($search)) > 0)
            """;

        return ReadPage(connection, transaction,
            "SELECT COUNT(*) " + from,
            """
            SELECT f.path, f.analyzer_id, f.analyzer_version, f.content_hash, f.symbol_count
            """ + " " + from + " ORDER BY f.path COLLATE BINARY LIMIT $limit OFFSET $offset;",
            parameters, offset, limit,
            reader => new DashboardFile(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4)),
            cancellationToken);
    }

    public DashboardPage<DashboardSymbol> GetSymbols(
        string projectId, string branch, string? search, string? kind, string? file,
        int offset, int limit, CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);
        using var connection = OpenProject(projectId);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshotId = RequireSnapshot(connection, transaction, branch);
        var summaryColumn = ReadSchemaVersion(connection, transaction) >= 4 ? "s.summary" : "NULL";
        var parameters = new (string Name, object? Value)[]
        {
            ("$snapshot", snapshotId), ("$search", EmptyToNull(search)),
            ("$kind", EmptyToNull(kind)), ("$file", EmptyToNull(file))
        };

        const string from = """
            FROM symbols s
            WHERE s.snapshot_id = $snapshot
              AND ($kind IS NULL OR s.kind = $kind COLLATE BINARY)
              AND ($file IS NULL OR s.path = $file COLLATE BINARY)
              AND ($search IS NULL OR instr(lower(s.qualified_name), lower($search)) > 0
                   OR instr(lower(s.path), lower($search)) > 0)
            """;

        return ReadPage(connection, transaction,
            "SELECT COUNT(*) " + from,
            $"""
            SELECT s.language, s.kind, s.name, s.qualified_name, s.path, s.line, s.column_number,
                   s.containing_type_line, s.containing_type_column, {summaryColumn}
            """ + " " + from + " ORDER BY s.qualified_name COLLATE BINARY, s.path COLLATE BINARY, " +
            "s.line, s.column_number, s.ordinal LIMIT $limit OFFSET $offset;",
            parameters, offset, limit,
            reader => new DashboardSymbol(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)),
            cancellationToken);
    }

    public DashboardPage<DashboardReference> GetReferences(
        string projectId, string branch, string? search, string? targetPath, int? targetLine, int? targetColumn,
        int offset, int limit, CancellationToken cancellationToken = default)
    {
        ValidatePage(offset, limit);

        if (targetPath is not null && (targetLine is null or < 1 || targetColumn is null or < 1))
        {
            throw new ArgumentException("Target path, line and column must identify one declaration.");
        }

        using var connection = OpenProject(projectId);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshotId = RequireSnapshot(connection, transaction, branch);

        if (ReadSchemaVersion(connection, transaction) < 3)
        {
            return new DashboardPage<DashboardReference>([], 0, offset, limit);
        }

        var parameters = new (string Name, object? Value)[]
        {
            ("$snapshot", snapshotId), ("$search", EmptyToNull(search)),
            ("$path", targetPath), ("$line", targetLine), ("$column", targetColumn)
        };

        const string from = """
            FROM symbol_references r
            WHERE r.snapshot_id = $snapshot
              AND ($path IS NULL OR (r.target_path = $path COLLATE BINARY
                   AND r.target_line = $line AND r.target_column = $column))
              AND ($search IS NULL OR instr(lower(r.target_path), lower($search)) > 0
                   OR instr(lower(r.usage_path), lower($search)) > 0
                   OR EXISTS (
                       SELECT 1 FROM symbols s
                       WHERE s.snapshot_id = r.snapshot_id AND s.path = r.target_path
                         AND s.line = r.target_line AND s.column_number = r.target_column
                         AND instr(lower(s.qualified_name), lower($search)) > 0))
            """;

        return ReadPage(connection, transaction,
            "SELECT COUNT(*) " + from,
            """
            SELECT r.target_path, r.target_line, r.target_column,
                   (SELECT s.qualified_name FROM symbols s
                    WHERE s.snapshot_id = r.snapshot_id AND s.path = r.target_path
                      AND s.line = r.target_line AND s.column_number = r.target_column
                    ORDER BY s.ordinal LIMIT 1),
                   r.usage_path, r.usage_line, r.usage_column
            """ + " " + from + " ORDER BY r.target_path COLLATE BINARY, r.target_line, r.target_column, " +
            "r.usage_path COLLATE BINARY, r.usage_line, r.usage_column LIMIT $limit OFFSET $offset;",
            parameters, offset, limit,
            reader => new DashboardReference(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
                reader.GetInt32(5), reader.GetInt32(6)),
            cancellationToken);
    }

    private SqliteConnection OpenProject(string id)
    {
        // Never accept an arbitrary path or a subdirectory chosen by an HTTP client.
        if (!CacheIdPattern.IsMatch(id))
        {
            throw new KeyNotFoundException("Unknown indexed project.");
        }

        var directory = Path.Combine(paths.Root, id);
        var database = Path.Combine(directory, "index.sqlite");

        if (!Directory.Exists(directory) || !File.Exists(database)
            || File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)
            || File.GetAttributes(database).HasFlag(FileAttributes.ReparsePoint))
        {
            throw new KeyNotFoundException("Unknown indexed project.");
        }

        // Probe the checkout identity before reusing the existing schema/identity validator.
        using var probe = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = database,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());

        probe.Open();
        var root = ReadRoot(probe);
        var expected = paths.GetDatabasePath(root);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (!string.Equals(expected, database, comparison))
        {
            throw new InvalidOperationException("Index cache directory does not match its repository identity.");
        }

        return IndexDatabase.Open(database, root, writable: false);
    }

    private static string ReadRoot(SqliteConnection connection)
    {
        using var command = IndexDatabase.Command(connection, null, "SELECT root FROM repository_info WHERE id = 1;");
        return command.ExecuteScalar() as string
            ?? throw new InvalidOperationException("Index repository identity is missing.");
    }

    private static int ReadSchemaVersion(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = IndexDatabase.Command(connection, transaction, "PRAGMA user_version;");
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<DashboardBranch> ReadBranches(
        SqliteConnection connection, int version, CancellationToken cancellationToken)
    {
        var referenceColumn = version >= 3 ? "s.reference_index_version" : "0";
        using var command = IndexDatabase.Command(connection, null, $"""
            SELECT b.branch, s.id, s.revision, s.semantic_language, s.completed_utc, s.file_count,
                   s.symbol_count, s.member_index_version, {referenceColumn}
            FROM active_branches b JOIN snapshots s ON s.id = b.snapshot_id
            ORDER BY b.branch COLLATE BINARY;
            """);

        var branches = new List<DashboardBranch>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            branches.Add(new DashboardBranch(reader.GetString(0), reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7) == 1,
                version >= 3 && reader.GetInt32(8) == 1));
        }

        return branches;
    }

    private static long RequireSnapshot(
        SqliteConnection connection, SqliteTransaction transaction, string branch)
    {
        using var command = IndexDatabase.Command(connection, transaction,
            "SELECT snapshot_id FROM active_branches WHERE branch = $branch COLLATE BINARY;",
            ("$branch", branch));

        return command.ExecuteScalar() is long id
            ? id : throw new KeyNotFoundException("No published index exists for this branch.");
    }

    private static IReadOnlyList<DashboardFacet> ReadFacets(
        SqliteConnection connection, SqliteTransaction transaction, long snapshotId,
        string column, CancellationToken cancellationToken)
    {
        // The column is supplied exclusively by GetSummary, never by an HTTP parameter.
        using var command = IndexDatabase.Command(connection, transaction, $"""
            SELECT {column}, COUNT(*) FROM symbols
            WHERE snapshot_id = $snapshot
            GROUP BY {column} ORDER BY COUNT(*) DESC, {column} COLLATE BINARY;
            """, ("$snapshot", snapshotId));

        var facets = new List<DashboardFacet>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            facets.Add(new DashboardFacet(reader.GetString(0), reader.GetInt64(1)));
        }

        return facets;
    }

    private static DashboardPage<T> ReadPage<T>(
        SqliteConnection connection, SqliteTransaction transaction,
        string countSql, string pageSql, (string Name, object? Value)[] parameters,
        int offset, int limit, Func<SqliteDataReader, T> map, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var count = IndexDatabase.Command(connection, transaction, countSql, parameters);
        var total = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture);

        var pageParameters = parameters.Concat(
            new (string Name, object? Value)[] { ("$limit", limit), ("$offset", offset) }).ToArray();
        using var command = IndexDatabase.Command(connection, transaction, pageSql, pageParameters);
        using var reader = command.ExecuteReader();
        var items = new List<T>();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(map(reader));
        }

        return new DashboardPage<T>(items, total, offset, limit);
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void ValidatePage(int offset, int limit)
    {
        if (offset < 0 || limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Pagination must use a non-negative offset and a limit from 1 to 200.");
        }
    }
}
