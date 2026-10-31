using CodeSemantic4Llm.Mcp.Server.Commands;
using CodeSemantic4Llm.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>
/// Exercises the actual SQLite read models across multiple checkouts and published branches,
/// including pagination and legacy indexes. No source files need to be opened or copied.
/// </summary>
public sealed class DashboardIndexReaderTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(
        Path.GetTempPath(), "codesemantic-dashboard-" + Guid.NewGuid().ToString("N"));
    private readonly IndexStoragePaths _paths;

    public DashboardIndexReaderTests()
    {
        _paths = new IndexStoragePaths(Path.Combine(_temporaryRoot, "cache"));
    }

    [Fact]
    public void DiscoversAllIndexedProjectsAndPublishedBranches()
    {
        var alpha = Seed("alpha", includeDevelop: true);
        var beta = Seed("beta");

        var projects = new SqliteIndexDashboardReader(_paths).ListProjects();

        Assert.Equal(2, projects.Count);
        var first = Assert.Single(projects, project => project.RootPath == alpha);
        Assert.Equal("alpha", first.Name);
        Assert.Equal(2, first.Branches.Count);
        Assert.Equal(["develop", "main"], first.Branches.Select(branch => branch.Name).ToArray());
        Assert.Equal(4, first.Branches.Single(branch => branch.Name == "main").SymbolCount);
        Assert.True(first.Branches.Single(branch => branch.Name == "main").HasReferences);
        Assert.Equal(beta, Assert.Single(projects, project => project.Name == "beta").RootPath);
    }

    [Fact]
    public void BrowsesFilesSymbolsAndReferencesWithPaginationAndFilters()
    {
        var root = Seed("alpha", includeDevelop: true);
        var projectId = Path.GetFileName(Path.GetDirectoryName(_paths.GetDatabasePath(root))!);
        var reader = new SqliteIndexDashboardReader(_paths);

        var summary = reader.GetSummary(projectId, "main");
        Assert.Equal(2, summary.ReferenceCount);
        Assert.Equal(2L, summary.Kinds.Single(facet => facet.Name == "method").Count);

        var files = reader.GetFiles(projectId, "main", "Calculator", 0, 10);
        Assert.Equal(1, files.Total);
        Assert.Equal("src/Calculator.cs", Assert.Single(files.Items).Path);
        Assert.Equal("dotnet-csharp", files.Items[0].AnalyzerId);
        Assert.Equal("abc123", files.Items[0].ContentHash);

        var symbols = reader.GetSymbols(projectId, "main", "Calculator", "method", null, 0, 1);
        Assert.Equal(2, symbols.Total);
        Assert.Single(symbols.Items);
        Assert.Equal("Add", symbols.Items[0].Name);
        Assert.Equal(1, symbols.Items[0].ContainingTypeLine);

        var next = reader.GetSymbols(projectId, "main", "Calculator", "method", null, 1, 1);
        Assert.Single(next.Items);
        Assert.Equal("Subtract", next.Items[0].Name);

        var usages = reader.GetReferences(projectId, "main", null, "src/Calculator.cs", 4, 5, 0, 1);
        Assert.Equal(2, usages.Total);
        Assert.Single(usages.Items);
        Assert.Equal("Calculator.Add", usages.Items[0].TargetQualifiedName);
        Assert.Equal("src/Program.cs", usages.Items[0].UsagePath);
        Assert.Equal(12, reader.GetReferences(projectId, "main", null,
            "src/Calculator.cs", 4, 5, 1, 1).Items[0].UsageLine);

        Assert.Equal(1, reader.GetSymbols(projectId, "develop", null, null, null, 0, 50).Total);
        Assert.Equal(0, reader.GetReferences(projectId, "develop", null, null, null, null, 0, 50).Total);
    }

    [Fact]
    public void UnknownProjectsAndAbsentCacheNeverCreateDatabases()
    {
        var reader = new SqliteIndexDashboardReader(_paths);
        Assert.Empty(reader.ListProjects());
        Assert.False(Directory.Exists(_paths.Root));

        Assert.Throws<KeyNotFoundException>(() =>
            reader.GetSymbols("../somewhere", "main", null, null, null, 0, 50));
        Assert.Throws<KeyNotFoundException>(() =>
            reader.SearchCode("../somewhere", "main", "snapshot", null, 10, TestContext.Current.CancellationToken));
        Assert.False(Directory.Exists(_paths.Root));
    }

    [Fact]
    public void VersionTwoIndexesRemainBrowsableWithoutAReferenceTable()
    {
        var root = Seed("legacy", schemaVersion: 2);
        var id = Path.GetFileName(Path.GetDirectoryName(_paths.GetDatabasePath(root))!);
        var reader = new SqliteIndexDashboardReader(_paths);

        var branch = Assert.Single(Assert.Single(reader.ListProjects()).Branches);
        Assert.False(branch.HasReferences);
        Assert.Equal(0, reader.GetSummary(id, "main").ReferenceCount);
        Assert.Equal(0, reader.GetReferences(id, "main", null, null, null, null, 0, 50).Total);
        Assert.Equal(4, reader.GetSymbols(id, "main", null, null, null, 0, 50).Total);

        var before = File.ReadAllBytes(_paths.GetDatabasePath(root));
        var error = Assert.Throws<InvalidOperationException>(() =>
            reader.SearchCode(id, "main", "Calculator", null, 10, TestContext.Current.CancellationToken));
        Assert.Contains("Run the index command again", error.Message);
        Assert.Equal(before, File.ReadAllBytes(_paths.GetDatabasePath(root)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void DashboardRejectsInvalidPorts(int port)
    {
        var settings = new IndexDashboardSettings { Port = port };
        Assert.False(settings.Validate().Successful);
    }

    [Fact]
    public void DashboardAcceptsExplicitPort()
    {
        var settings = new IndexDashboardSettings { Port = 6000 };
        Assert.True(settings.Validate().Successful);
    }

    private string Seed(string name, bool includeDevelop = false, int schemaVersion = 3)
    {
        var root = Path.GetFullPath(Path.Combine(_temporaryRoot, "repositories", name));
        Directory.CreateDirectory(root);
        var databasePath = _paths.GetDatabasePath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA user_version = {schemaVersion};
            CREATE TABLE repository_info (id INTEGER PRIMARY KEY, root TEXT NOT NULL);
            CREATE TABLE snapshots (
                id INTEGER PRIMARY KEY, branch TEXT NOT NULL, revision TEXT,
                semantic_language TEXT NOT NULL, completed_utc TEXT,
                file_count INTEGER NOT NULL, symbol_count INTEGER NOT NULL,
                member_index_version INTEGER NOT NULL
                {(schemaVersion == 3 ? ", reference_index_version INTEGER NOT NULL" : "")}
            );
            CREATE TABLE active_branches (branch TEXT PRIMARY KEY, snapshot_id INTEGER NOT NULL);
            CREATE TABLE indexed_files (
                snapshot_id INTEGER NOT NULL, path TEXT NOT NULL, content_hash TEXT NOT NULL,
                analyzer_id TEXT NOT NULL, analyzer_version TEXT NOT NULL, symbol_count INTEGER NOT NULL
            );
            CREATE TABLE symbols (
                snapshot_id INTEGER NOT NULL, path TEXT NOT NULL, ordinal INTEGER NOT NULL,
                language TEXT NOT NULL, kind TEXT NOT NULL, name TEXT NOT NULL,
                qualified_name TEXT NOT NULL, line INTEGER NOT NULL, column_number INTEGER NOT NULL,
                containing_type_line INTEGER, containing_type_column INTEGER
            );
            """;
        command.ExecuteNonQuery();

        if (schemaVersion == 3)
        {
            command.CommandText = """
                CREATE TABLE symbol_references(
                    snapshot_id INTEGER NOT NULL,
                    target_path TEXT NOT NULL, target_line INTEGER NOT NULL, target_column INTEGER NOT NULL,
                    usage_path TEXT NOT NULL, usage_line INTEGER NOT NULL, usage_column INTEGER NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        command.CommandText = "INSERT INTO repository_info(id,root) VALUES(1,$root);";
        command.Parameters.AddWithValue("$root", root);
        command.ExecuteNonQuery();
        command.Parameters.Clear();

        var referenceColumn = schemaVersion == 3 ? ", reference_index_version" : "";
        var referenceValue = schemaVersion == 3 ? ", 1" : "";
        command.CommandText = $"""
            INSERT INTO snapshots(id,branch,revision,semantic_language,completed_utc,file_count,symbol_count,member_index_version{referenceColumn})
            VALUES(10,'main','abcdef','en','2026-10-30T10:00:00Z',2,4,1{referenceValue});
            INSERT INTO active_branches(branch,snapshot_id) VALUES('main',10);
            INSERT INTO indexed_files VALUES
                (10,'src/Calculator.cs','abc123','dotnet-csharp','1.0',3),
                (10,'src/Program.cs','def456','dotnet-csharp','1.0',1);
            INSERT INTO symbols VALUES
                (10,'src/Calculator.cs',0,'csharp','class','Calculator','Calculator',1,1,NULL,NULL),
                (10,'src/Calculator.cs',1,'csharp','method','Add','Calculator.Add',4,5,1,1),
                (10,'src/Calculator.cs',2,'csharp','method','Subtract','Calculator.Subtract',8,5,1,1),
                (10,'src/Program.cs',0,'csharp','class','Program','Program',2,1,NULL,NULL);
            """;
        command.ExecuteNonQuery();

        if (schemaVersion == 3)
        {
            command.CommandText = """
                INSERT INTO symbol_references VALUES
                    (10,'src/Calculator.cs',4,5,'src/Program.cs',7,3),
                    (10,'src/Calculator.cs',4,5,'src/Program.cs',12,3);
                """;
            command.ExecuteNonQuery();
        }

        if (includeDevelop)
        {
            command.CommandText = $"""
                INSERT INTO snapshots(id,branch,revision,semantic_language,completed_utc,file_count,symbol_count,member_index_version{referenceColumn})
                VALUES(11,'develop','123456','fr','2026-10-30T11:00:00Z',1,1,1{(schemaVersion == 3 ? ", 0" : "")});
                INSERT INTO active_branches(branch,snapshot_id) VALUES('develop',11);
                INSERT INTO indexed_files VALUES(11,'src/Service.cs','xyz123','dotnet-csharp','1.0',1);
                INSERT INTO symbols VALUES(11,'src/Service.cs',0,'csharp','class','Service','Service',1,1,NULL,NULL);
                """;
            command.ExecuteNonQuery();
        }

        return root;
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot))
        {
            Directory.Delete(_temporaryRoot, recursive: true);
        }
    }
}
