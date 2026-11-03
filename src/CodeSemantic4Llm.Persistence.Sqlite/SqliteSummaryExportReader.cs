using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>Exports derived descriptions under one read transaction, without copying source text.</summary>
public sealed class SqliteSummaryExportReader(IndexStoragePaths paths) : ISummaryExportReader
{
    public IReadOnlyList<SharedSummaryFile> Read(
        RepositoryState state, string configurationFingerprint, SummaryProfile profile,
        CancellationToken cancellationToken = default)
    {
        using var connection = IndexDatabase.Open(paths.GetDatabasePath(state.Scope.RootPath), state.Scope.RootPath, writable: false);
        using var transaction = connection.BeginTransaction(deferred: true);
        var snapshot = SqliteIndexStore.ReadSnapshot(connection, transaction, state.Scope)
            ?? throw new InvalidOperationException("Run index before exporting shared summaries.");
        using var identity = IndexDatabase.Command(connection, transaction,
            "SELECT configuration_hash FROM snapshots WHERE id = $id;", ("$id", snapshot.SnapshotId));
        if (identity.ExecuteScalar() as string != configurationFingerprint || snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("The saved index does not match this checkout/configuration. Run index before export.");
        }

        var sources = new List<IndexedSourceFile>();
        using (var command = IndexDatabase.Command(connection, transaction, """
            SELECT path, content_hash, analyzer_id, analyzer_version FROM indexed_files
            WHERE snapshot_id = $id ORDER BY path;
            """, ("$id", snapshot.SnapshotId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                sources.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var files = new List<SharedSummaryFile>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var command = IndexDatabase.Command(connection, transaction, """
                SELECT qualified_name, kind, line, column_number, summary FROM symbols
                WHERE snapshot_id = $id AND path = $path AND summary IS NOT NULL AND length(trim(summary)) > 0
                ORDER BY ordinal;
                """, ("$id", snapshot.SnapshotId), ("$path", source.RelativePath));
            using var reader = command.ExecuteReader();
            var summaries = new List<SharedSymbolSummary>();
            while (reader.Read())
            {
                summaries.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetString(4)));
            }

            files.Add(new(source, profile, summaries));
        }

        return files;
    }
}
