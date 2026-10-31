using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Verifies documentation precedence with real parsing and persistence and a recording LLM substitute.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private readonly RecordingSummaryGenerator _summaryGenerator = new();

    [Given("summary generation is (enabled|disabled) with forceGeneration \"(.*)\"")]
    [When("summary generation is (enabled|disabled) with forceGeneration \"(.*)\"")]
    public void ConfigureSummaryGeneration(string state, string forceGeneration)
    {
        File.WriteAllText(Path.Combine(_root, "codesemantic4llm.yaml"), DefaultYaml +
            "semanticIndexing:\n  language: fr\n  llm:\n" +
            $"    enabled: {(state == "enabled" ? "true" : "false")}\n    forceGeneration: {forceGeneration}\n" +
            "    baseUrl: http://localhost:1234/v1\n    model: test-model\n");
    }

    [Then("declaration \"(.*)\" has stored summary \"(.*)\"")]
    public void ThenStoredSummary(string name, string expected) =>
        Assert.Equal(expected, Assert.Single(Store.FindSymbols(_result!.Snapshot.Scope, name, 20, Token).Symbols).Summary);

    [Then("declaration \"(.*)\" has no stored summary")]
    public void ThenNoSummary(string name) =>
        Assert.Null(Assert.Single(Store.FindSymbols(_result!.Snapshot.Scope, name, 20, Token).Symbols).Summary);

    [Then(@"the LLM has summarized (\d+) declarations")]
    public void ThenSummaryCount(int count) => Assert.Equal(count, _summaryGenerator.CallCount);

    [Then("summary search \"(.*)\" finds \"(.*)\"")]
    public void ThenSummarySearch(string query, string name) =>
        Assert.Equal(name, Assert.Single(Store.SearchCode(_result!.Snapshot.Scope, query, null, 20, Token).Symbols).Name);

    [Then("summary search \"(.*)\" finds nothing")]
    public void ThenNoSummarySearch(string query) =>
        Assert.Empty(Store.SearchCode(_result!.Snapshot.Scope, query, null, 20, Token).Symbols);

    [Then("branch \"(.*)\" retains summary \"(.*)\" for \"(.*)\"")]
    public void ThenBranchSummary(string branch, string summary, string name) =>
        Assert.Equal(summary, Assert.Single(Store.FindSymbols(new RepositoryScope(_root, branch), name, 20, Token).Symbols).Summary);

    private sealed class RecordingSummaryGenerator : ISymbolSummaryGenerator
    {
        public int CallCount { get; private set; }

        public Task<string> SummarizeAsync(SymbolDefinition symbol, string declarationSource,
            SemanticIndexingConfiguration configuration, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult($"Generated summary for {symbol.QualifiedName}.");
        }
    }
}
