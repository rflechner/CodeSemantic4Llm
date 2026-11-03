using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Persistence.Sqlite;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises the dashboard adapter against real branch indexes without source or model access.</summary>
[Binding, Scope(Tag = "dashboard-search")]
public sealed class DashboardCodeSearchSteps
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "dashboard-search-" + Guid.NewGuid().ToString("N"));
    private readonly IndexStoragePaths _paths;
    private readonly SqliteIndexStore _store;
    private readonly SqliteIndexDashboardReader _reader;
    private DashboardCodeSearchResult? _result;
    private Exception? _error;
    private RepositoryState? _selectedState;
    private string _query = "";
    private string? _kind;
    private int _limit;

    public DashboardCodeSearchSteps()
    {
        _paths = new IndexStoragePaths(Path.Combine(_workspace, "cache"));
        _store = new SqliteIndexStore(_paths);
        _reader = new SqliteIndexDashboardReader(_paths);
    }

    [Given("the dashboard cache contains these declarations:")]
    public void GivenDeclarations(Table table)
    {
        foreach (var group in table.Rows.GroupBy(row => new RepositoryScope(Root(row["project"]), row["branch"])))
        {
            Directory.CreateDirectory(group.Key.RootPath);
            using var update = _store.BeginUpdate(new RepositoryState(group.Key, "saved-head"), "config", "en");

            foreach (var row in group)
            {
                var path = "src/" + row["name"] + ".cs";
                var summary = string.IsNullOrEmpty(row["summary"]) ? null : row["summary"];
                update.AddFile(new IndexedSourceFile(path, "hash", "dotnet-csharp", "test"),
                [
                    new SymbolDefinition("csharp", row["kind"], row["name"], "Demo." + row["name"],
                        new SourceLocation(path, 7, 5),
                        row["kind"] == "method" ? new SourceLocation(path, 1, 1) : null, summary)
                ]);
            }

            update.Publish();
        }
    }

    [Given("the original dashboard checkouts are unavailable")]
    public void GivenUnavailableCheckouts() => TemporaryDirectory.Remove(Path.Combine(_workspace, "repositories"));

    [When(@"the dashboard searches ""(.*)"" branch ""(.*)"" for ""(.*)"" with kind ""(.*)"" and limit (\d+)")]
    public void WhenSearch(string project, string branch, string query, string kind, int limit)
    {
        var cached = Assert.Single(_reader.ListProjects(), candidate => candidate.Name == project);
        _selectedState = new RepositoryState(new RepositoryScope(cached.RootPath, branch), "saved-head");
        _query = query;
        _kind = kind;
        _limit = limit;
        _error = Record.Exception(() => _result = _reader.SearchCode(cached.Id, branch, query, kind, limit));
    }

    [Then(@"the dashboard search matches are ""(.*)"" in that order")]
    public void ThenMatches(string names)
    {
        Assert.Null(_error);
        Assert.NotNull(_result);
        Assert.Equal(names.Split(", ", StringSplitOptions.RemoveEmptyEntries), _result.Symbols.Select(symbol => symbol.Name));
    }

    [Then("the dashboard search matches the MCP search use case")]
    public async Task ThenMatchesMcpSearch()
    {
        var search = new SearchCode(_store, new SavedRepositoryStateProvider(_selectedState!));
        var expected = await search.ExecuteAsync(_selectedState!.Scope.RootPath, _query, _kind, _limit,
            TestContext.Current.CancellationToken);
        AssertSnapshot(expected.Snapshot);
        Assert.Equal(expected.HasMore, _result!.HasMore);
        Assert.Equal(expected.Symbols.Select(symbol => new DashboardSymbol(
            symbol.Language, symbol.Kind, symbol.Name, symbol.QualifiedName,
            symbol.Location.RelativePath, symbol.Location.Line, symbol.Location.Column,
            symbol.ContainingType?.Line, symbol.ContainingType?.Column, symbol.Summary)), _result.Symbols);
    }

    [Then("the dashboard search is complete")]
    public void ThenComplete() => Assert.False(_result!.HasMore);

    [Then("the dashboard search reports more matches")]
    public void ThenHasMore() => Assert.True(_result!.HasMore);

    [Then("the first dashboard match has no summary")]
    public void ThenNoSummary() => Assert.Null(_result!.Symbols[0].Summary);

    [Then(@"the dashboard search identifies the published snapshot for ""(.*)"" branch ""(.*)""")]
    public void ThenSnapshot(string project, string branch) =>
        AssertSnapshot(_store.GetSnapshot(new RepositoryScope(Root(project), branch))!);

    private void AssertSnapshot(RepositoryIndexSnapshot expected) =>
        Assert.Equal(new DashboardSnapshot(expected.Scope, expected.SnapshotId, expected.RecordedRevision,
            expected.SemanticLanguage, expected.CompletedAt, expected.FileCount, expected.SymbolCount), _result!.Snapshot);

    [Then("the dashboard search rejects the invalid request")]
    public void ThenInvalid() => Assert.IsAssignableFrom<ArgumentException>(_error);

    [AfterScenario]
    public void Cleanup()
    {
        TemporaryDirectory.TryRemove(_workspace);
    }

    private string Root(string project) => Path.Combine(_workspace, "repositories", project);

    private sealed class SavedRepositoryStateProvider(RepositoryState state) : IRepositoryStateProvider
    {
        public Task<RepositoryState> GetStateAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(state);
    }
}
