using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>
/// Exercises SQLite FTS5 against actual published and reused symbol snapshots.
/// All sample summaries describe the CodeSemantic4Llm indexing/navigation domain.
/// </summary>
public sealed class FullTextSearchTests : IDisposable
{
    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "CodeSemantic4Llm.FtsTests", Guid.NewGuid().ToString("N"));
    private readonly IndexStoragePaths _paths;
    private readonly SqliteIndexStore _store;
    private readonly string _root;

    public FullTextSearchTests()
    {
        _root = Path.Combine(_workspace, "repository");
        Directory.CreateDirectory(_root);
        _paths = new IndexStoragePaths(Path.Combine(_workspace, "cache"));
        _store = new SqliteIndexStore(_paths);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dashboard_reads_complete_files_during_and_after_an_interrupted_run(bool hasPublishedIndex)
    {
        var token = TestContext.Current.CancellationToken;
        var scope = Scope("main");
        var file = File("New.cs", "hash", Symbol("class", "NewType", "New.cs", 1, null));
        var previous = hasPublishedIndex ? Publish(scope, [File("Old.cs", "old")]) : null;
        var reader = new SqliteIndexDashboardReader(_paths);
        using (var update = _store.BeginUpdate(State(scope), "config", "en", token))
        {
            update.AddFile(file.File, file.Symbols, token);
            var project = Assert.Single(reader.ListProjects(token));
            var branch = Assert.Single(project.Branches);
            Assert.Null(branch.CompletedAt);
            Assert.False(branch.HasReferences);
            Assert.Equal(1, branch.FileCount);
            Assert.Equal("New.cs", Assert.Single(reader.GetFiles(project.Id, "main", null, 0, 10, token).Items).Path);
            Assert.Equal("NewType", Assert.Single(reader.SearchCode(project.Id, "main", "NewType", null, 10, token).Symbols).Name);
            Assert.Equal(previous, _store.GetSnapshot(scope, token));
            var invalid = File("Bad.cs", "bad", Symbol("class", "Invalid", "Different.cs", 1, null));
            Assert.Throws<InvalidOperationException>(() => update.AddFile(invalid.File, invalid.Symbols, token));
        }

        var preview = Assert.Single(reader.ListProjects(token));
        Assert.Null(Assert.Single(preview.Branches).CompletedAt);
        Assert.Single(reader.GetFiles(preview.Id, "main", null, 0, 10, token).Items);
        Assert.Equal(previous, _store.GetSnapshot(scope, token));
        var published = Publish(scope, [File("Final.cs", "final")]);
        Assert.Equal(published.CompletedAt, Assert.Single(Assert.Single(reader.ListProjects(token)).Branches).CompletedAt);
        Assert.Equal("Final.cs", Assert.Single(reader.GetFiles(preview.Id, "main", null, 0, 10, token).Items).Path);
    }

    [Fact]
    public void Ranks_relevant_summaries_and_identifiers_without_reading_source()
    {
        var scope = Scope("main");
        Publish(scope,
        [
            File("src/IndexRepository.cs", "hash-one",
                Symbol("class", "IndexRepository", "src/IndexRepository.cs", 4,
                    "Indexes C# source files into a branch-specific SQLite metadata snapshot."),
                Symbol("method", "PublishSnapshot", "src/IndexRepository.cs", 12,
                    "Publishes the branch snapshot and atomically replaces its predecessor.")),
            File("src/FindUsages.cs", "hash-two",
                Symbol("method", "FindUsages", "src/FindUsages.cs", 8,
                    "Finds Roslyn-resolved references to a method or type in the code index.")),
            File("src/SourceReader.cs", "hash-three",
                Symbol("method", "ReadSource", "src/SourceReader.cs", 9,
                    "Reads the original source document from the working tree."))
        ]);

        var results = _store.SearchCode(scope, "Where do we publish a branch snapshot?", null, 10);
        Assert.Equal("PublishSnapshot", results.Symbols[0].Name);
        Assert.Equal(2, results.Symbols.Count);
        Assert.Contains("atomically", results.Symbols[0].Summary);

        var filtered = _store.SearchCode(scope, "C# methods that index a repository", "class", 10);
        var type = Assert.Single(filtered.Symbols);
        Assert.Equal("IndexRepository", type.Name);
        Assert.Equal("class", type.Kind);

        var references = _store.SearchCode(scope, "references resolved by Roslyn", "method", 10);
        Assert.Equal("FindUsages", references.Symbols[0].Name);
        Assert.Equal("src/FindUsages.cs", references.Symbols[0].Location.RelativePath);
    }

    [Fact]
    public void Finds_unicode_text_with_diacritics_and_treats_operators_as_literal_input()
    {
        var scope = Scope("main");
        Publish(scope,
        [
            File("src/FindUsages.cs", "hash",
                Symbol("method", "FindUsages", "src/FindUsages.cs", 1,
                    "Localise les références des méthodes avec le modèle sémantique Roslyn."))
        ]);

        var french = _store.SearchCode(scope, "Où sont les REFERENCES des méthodes ?", "method", 10);
        Assert.Equal("FindUsages", Assert.Single(french.Symbols).Name);

        // The input is never interpolated into either an FTS expression or SQL.
        var hostile = _store.SearchCode(scope,
            "references\" OR summary:secret); DROP TABLE symbols; --", null, 10);
        Assert.Contains(hostile.Symbols, symbol => symbol.Name == "FindUsages");
        Assert.Single(_store.FindSymbols(scope, "FindUsages", 10).Symbols);
    }

    [Fact]
    public void Searches_names_without_summaries_and_bounds_results()
    {
        var scope = Scope("main");
        Publish(scope,
        [
            File("src/First.cs", "one", Symbol("method", "FindSymbols", "src/First.cs", 1, null)),
            File("src/Second.cs", "two", Symbol("method", "FindSymbols", "src/Second.cs", 1, null))
        ]);

        var first = _store.SearchCode(scope, "FindSymbols", "method", 1);
        Assert.Single(first.Symbols);
        Assert.True(first.HasMore);
        Assert.Equal("src/First.cs", first.Symbols[0].Location.RelativePath);
        Assert.Null(first.Symbols[0].Summary);
        Assert.Equal(2, _store.SearchCode(scope, "FindSymbols", null, 10).Symbols.Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => _store.SearchCode(scope, "FindSymbols", null, 201));
        Assert.Throws<ArgumentException>(() => _store.SearchCode(scope, "where is the", null, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _store.SearchCode(scope, new string('a', 513), null, 10));
    }

    [Fact]
    public void Index_is_isolated_by_branch_and_by_repository()
    {
        var main = Scope("main");
        var feature = Scope("feature/full-text");
        Publish(main,
        [
            File("src/IndexRepository.cs", "main",
                Symbol("method", "IndexRepository", "src/IndexRepository.cs", 1,
                    "Indexes a checked-out repository on the main branch."))
        ]);

        Publish(feature,
        [
            File("src/FindReferences.cs", "feature",
                Symbol("method", "FindReferences", "src/FindReferences.cs", 1,
                    "Finds references on the feature branch."))
        ]);

        Assert.Empty(_store.SearchCode(main, "references", null, 10).Symbols);
        Assert.Empty(_store.SearchCode(feature, "repository", null, 10).Symbols);
        Assert.Equal("IndexRepository", Assert.Single(_store.SearchCode(main, "repository", null, 10).Symbols).Name);
        Assert.Equal("FindReferences", Assert.Single(_store.SearchCode(feature, "references", null, 10).Symbols).Name);

        var otherRoot = Path.Combine(_workspace, "other-repository");
        Directory.CreateDirectory(otherRoot);
        var other = new RepositoryScope(otherRoot, "main");
        Publish(other, [File("src/TreeParser.cs", "other",
            Symbol("class", "TreeParser", "src/TreeParser.cs", 1,
                "Parses the syntax tree for a separate checkout."))]);

        Assert.Empty(_store.SearchCode(main, "syntax tree", null, 10).Symbols);
        Assert.Equal("TreeParser", Assert.Single(_store.SearchCode(other, "syntax tree", null, 10).Symbols).Name);
    }

    [Fact]
    public void Reuse_changes_deletions_and_rollback_keep_fts_in_sync_with_published_snapshots()
    {
        var scope = Scope("main");
        var unchanged = File("src/FindUsages.cs", "unchanged",
            Symbol("method", "FindUsages", "src/FindUsages.cs", 1,
                "Finds references in a code repository."));
        var original = File("src/IndexRepository.cs", "v1",
            Symbol("method", "PublishBranch", "src/IndexRepository.cs", 1,
                "Publishes the active branch snapshot."));
        var initial = Publish(scope, [unchanged, original]);

        using (var update = _store.BeginUpdate(State(scope), "config", "en"))
        {
            Assert.True(update.TryReuse(unchanged.File));
            Assert.True(update.TryReuse(original.File));
            update.Publish();
        }

        Assert.Equal("PublishBranch", Assert.Single(_store.SearchCode(scope, "publishes", null, 10).Symbols).Name);

        var replacement = File("src/IndexRepository.cs", "v2",
            Symbol("method", "ValidateCheckout", "src/IndexRepository.cs", 1,
                "Verifies the repository state before indexing."));
        using (var update = _store.BeginUpdate(State(scope), "config", "en"))
        {
            Assert.True(update.TryReuse(unchanged.File));
            Assert.False(update.TryReuse(replacement.File));
            update.AddFile(replacement.File, replacement.Symbols);
            var next = update.Publish();
            Assert.NotEqual(initial.SnapshotId, next.SnapshotId);
        }

        Assert.Empty(_store.SearchCode(scope, "publishes", null, 10).Symbols);
        Assert.Equal("ValidateCheckout", Assert.Single(_store.SearchCode(scope, "verifies", null, 10).Symbols).Name);
        Assert.Equal("FindUsages", Assert.Single(_store.SearchCode(scope, "references", null, 10).Symbols).Name);

        using (var update = _store.BeginUpdate(State(scope), "config", "en"))
        {
            update.AddFile(
                new IndexedSourceFile("src/Temporary.cs", "temporary", "dotnet-csharp", "test"),
                [Symbol("method", "TemporarySymbol", "src/Temporary.cs", 1,
                    "Temporary experimental declaration that must not be visible.")]);
            Assert.Empty(_store.SearchCode(scope, "experimental", null, 10).Symbols);
            // No Publish: releasing this update rolls back FTS5 and SQL rows together.
        }

        Assert.Empty(_store.SearchCode(scope, "experimental", null, 10).Symbols);

        using (var update = _store.BeginUpdate(State(scope), "config", "en"))
        {
            Assert.True(update.TryReuse(unchanged.File));
            // Removed source files are absent from the new generation.
            update.Publish();
        }

        Assert.Empty(_store.SearchCode(scope, "verifies", null, 10).Symbols);
        Assert.Single(_store.SearchCode(scope, "references", null, 10).Symbols);
    }

    [Fact]
    public void Upgrading_schema_four_rebuilds_existing_summaries_without_an_llm_call()
    {
        var scope = Scope("main");
        var file = File("src/SymbolIndex.cs", "content-hash",
            Symbol("method", "SearchIndex", "src/SymbolIndex.cs", 1,
                "Searches indexed C# declarations by their stored summaries."));
        Publish(scope, [file]);

        // Simulate an actual v4 cache: no FTS5 table or maintenance triggers.
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = _paths.GetDatabasePath(_root),
                   Mode = SqliteOpenMode.ReadWrite,
                   Pooling = false
               }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TRIGGER symbol_search_insert;
                DROP TRIGGER symbol_search_update;
                DROP TRIGGER symbol_search_delete;
                DROP TABLE symbol_search;
                PRAGMA user_version = 4;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Equal("SearchIndex", Assert.Single(_store.FindSymbols(scope, "SearchIndex", 10).Symbols).Name);
        var missingFts = Assert.Throws<InvalidOperationException>(() =>
            _store.SearchCode(scope, "declarations", null, 10));
        Assert.Contains("Run the index command again", missingFts.Message);

        using (var update = _store.BeginUpdate(State(scope), "config", "en"))
        {
            Assert.True(update.TryReuse(file.File));
            update.Publish();
        }

        Assert.Equal("SearchIndex", Assert.Single(_store.SearchCode(scope, "declarations", null, 10).Symbols).Name);
        using var verify = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _paths.GetDatabasePath(_root), Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        verify.Open();
        using var version = verify.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(7L, version.ExecuteScalar());
    }

    private RepositoryScope Scope(string branch) => new(_root, branch);

    private static RepositoryState State(RepositoryScope scope) => new(scope, "fixed-head");

    private static (IndexedSourceFile File, IReadOnlyList<SymbolDefinition> Symbols) File(
        string path, string hash, params SymbolDefinition[] symbols) =>
        (new IndexedSourceFile(path, hash, "dotnet-csharp", "test"), symbols);

    private static SymbolDefinition Symbol(
        string kind, string name, string path, int line, string? summary) =>
        new("csharp", kind, name, "CodeSemantic4Llm." + name,
            new SourceLocation(path, line, 1), Summary: summary);

    private RepositoryIndexSnapshot Publish(
        RepositoryScope scope,
        (IndexedSourceFile File, IReadOnlyList<SymbolDefinition> Symbols)[] files)
    {
        using var update = _store.BeginUpdate(State(scope), "config", "en");

        foreach (var file in files)
        {
            update.AddFile(file.File, file.Symbols);
        }

        return update.Publish();
    }

    public void Dispose()
    {
        TemporaryDirectory.TryRemove(_workspace);
    }
}
