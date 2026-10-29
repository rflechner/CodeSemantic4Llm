using Microsoft.Data.Sqlite;

namespace CodeSemantic4Llm.Persistence.Sqlite;

/// <summary>
/// General-purpose SQLite connection boundary. Repository snapshot storage is managed by SqliteIndexStore.
/// </summary>
public sealed class SqliteConnectionFactory
{
    public SqliteConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(DatabasePath)!;
        Directory.CreateDirectory(directory);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        var connection = new SqliteConnection(builder.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
