using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Black-box Reqnroll coverage for cumulative published folder indexing.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private RepositoryIndexRun? _cumulativeRun;
    private Exception? _cumulativeFailure;

    [When(@"I incrementally index folder ""(.*)""")]
    public async Task WhenIncrementalFolderIndex(string folder)
    {
        _analyzer.AnalysisCount = 0;
        _cumulativeRun = await CreateIndexer().RunAsync(
            _root, folder, cancellationToken: Token);
        Assert.NotNull(_cumulativeRun.PublishedSnapshot);
    }

    [When(@"I attempt to incrementally index folder ""(.*)""")]
    public async Task WhenAttemptIncrementalFolderIndex(string folder)
    {
        _cumulativeFailure = await Record.ExceptionAsync(() =>
            CreateIndexer().RunAsync(_root, folder, cancellationToken: Token));
    }

    [Then(@"the cumulative partial index contains (\d+) files")]
    public async Task ThenCumulativeIndexFileCount(int count)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var snapshot = Store.GetSnapshot(state.Scope);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsPartial);
        Assert.Equal(count, snapshot.FileCount);
        Assert.Equal(state.Revision, snapshot.RecordedRevision);
    }

    [Then(@"the status has a published partial snapshot with (\d+) files")]
    public void ThenPartialIndexStatus(int count)
    {
        Assert.NotNull(_status?.Snapshot);
        Assert.True(_status.Snapshot.IsPartial);
        Assert.Equal(count, _status.Snapshot.FileCount);
    }

    [Then(@"the partial snapshot contains declaration ""(.*)""")]
    public async Task ThenPartialContainsDeclaration(string name)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var result = Store.FindSymbols(state.Scope, name, 20);
        Assert.Equal(name, Assert.Single(result.Symbols).Name);
        Assert.True(result.Snapshot.IsPartial);
    }

    [Then(@"the partial snapshot does not contain declaration ""(.*)""")]
    public async Task ThenPartialDoesNotContainDeclaration(string name)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        Assert.Empty(Store.FindSymbols(state.Scope, name, 20).Symbols);
    }

    [Then(@"the last folder index analyzed only (\d+) files?")]
    public void ThenOnlyFolderWasAnalyzed(int count)
    {
        Assert.Equal(count, _cumulativeRun!.AnalyzedFiles);
        Assert.Equal(count, _analyzer.AnalysisCount);
    }

    [Then(@"FTS5 search of the partial snapshot finds ""(.*)""")]
    public async Task ThenPartialFullTextSearch(string name)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var result = Store.SearchCode(state.Scope, name, null, 10);
        Assert.Equal(name, Assert.Single(result.Symbols).Name);
        Assert.True(result.Snapshot.IsPartial);
    }

    [Then("partial indexing is rejected without replacing the published snapshot")]
    public void ThenPartialSettingsMismatchDoesNotOverwrite()
    {
        Assert.IsType<InvalidOperationException>(_cumulativeFailure);
        Assert.Contains("different indexing settings", _cumulativeFailure.Message);
        Assert.Equal(_cumulativeRun!.PublishedSnapshot!.SnapshotId,
            Store.GetSnapshot(_cumulativeRun.State.Scope)!.SnapshotId);
    }

    [Then("partial reference lookup is reported as unavailable")]
    public async Task ThenPartialReferencesAreUnavailable()
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var indexed = Store.FindSymbols(state.Scope, "NavigationIndex", 1);
        var declaration = Assert.Single(indexed.Symbols);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Store.FindUsages(state.Scope, declaration.Location, indexed.Snapshot.SnapshotId, 10, 0));
        Assert.Contains("no reference metadata", exception.Message);
    }

    [Then(@"the resulting index is complete with (\d+) files")]
    public async Task ThenCompleteIndexAfterPartial(int count)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var snapshot = Store.GetSnapshot(state.Scope);
        Assert.NotNull(snapshot);
        Assert.False(snapshot.IsPartial);
        Assert.Equal(count, snapshot.FileCount);
    }
}
