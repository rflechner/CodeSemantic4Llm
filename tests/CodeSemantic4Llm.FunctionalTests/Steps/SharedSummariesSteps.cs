using CodeSemantic4Llm.Application.UseCases;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

[Binding]
public sealed class SharedSummariesSteps : IDisposable
{
    private readonly SharedSummaryTestRepository _repo = new();
    private RepositoryIndexingResult? _result;
    private int _initialCalls;

    [Given("a checkout with exported shared summaries")]
    public async Task GivenSharedSummaries()
    {
        await _repo.InitializeAsync();
        await _repo.Indexer.ExecuteAsync(_repo.Root, cancellationToken: _repo.Token);
        await _repo.Exporter.ExecuteAsync(_repo.Root, cancellationToken: _repo.Token);
        _initialCalls = _repo.Generator.Calls;
    }

    [When("another developer indexes it with a fresh local database")]
    public async Task WhenFreshDatabase()
    {
        _repo.UseFreshDatabase();
        _repo.Generator.Fail = true;
        _result = await _repo.Indexer.ExecuteAsync(_repo.Root, cancellationToken: _repo.Token);
    }

    [When("a colleague's updated Markdown description arrives")]
    public async Task WhenMarkdownArrives()
    {
        File.WriteAllText(_repo.SummaryPath, File.ReadAllText(_repo.SummaryPath).Replace("nebula", "quasar"));
        _result = await _repo.Indexer.ExecuteAsync(_repo.Root, cancellationToken: _repo.Token);
    }

    [Then("the shared descriptions are searchable without additional LLM calls")]
    public void ThenSharedDescriptions() => AssertSearch("nebula");

    [Then("search uses the updated description without additional LLM calls")]
    public void ThenUpdatedDescription()
    {
        AssertSearch("quasar");
        Assert.Empty(_repo.Store.SearchCode(_result!.Snapshot.Scope, "nebula", null, 10, _repo.Token).Symbols);
    }

    private void AssertSearch(string query)
    {
        Assert.Equal(_initialCalls, _repo.Generator.Calls);
        Assert.Equal(2, _repo.Store.SearchCode(_result!.Snapshot.Scope, query, null, 10, _repo.Token).Symbols.Count);
    }

    public void Dispose() => _repo.Dispose();
}
