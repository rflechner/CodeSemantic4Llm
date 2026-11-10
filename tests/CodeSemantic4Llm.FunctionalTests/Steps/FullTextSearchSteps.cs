using System.Text.Json;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Persistence.Sqlite;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>End-to-end full-text navigation with and without MCP indexing administration.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private JsonElement _fullTextReply;
    private bool _fullTextError;
    private readonly List<(string[] ExpectedKinds, string[] ActualKinds)> _kindSearchResults = [];

    [Given("the checkout has saved JSON navigation summaries")]
    public async Task GivenJsonNavigationSummaries()
    {
        const string path = "src/NavigationIndex.cs";
        WriteSource(path, "namespace Demo; class NavigationIndex { void Load() {} }");
        var state = await _stateProvider.GetStateAsync(_root, Token);
        using var update = Store.BeginUpdate(state, "test-config", "fr", Token);
        update.AddFile(new IndexedSourceFile(path, "test-hash", "dotnet-csharp", "test"),
        [
            new SymbolDefinition("csharp", "class", "NavigationIndex", "Demo.NavigationIndex",
                new SourceLocation(path, 1, 23), Summary: "Gère un **index de navigation JSON**."),
            new SymbolDefinition("csharp", "method", "Load", "Demo.NavigationIndex.Load",
                new SourceLocation(path, 1, 46), new SourceLocation(path, 1, 23),
                "Charge un index de navigation JSON depuis un fichier.")
        ], Token);
        update.Publish(Token);
    }

    [When(@"MCP in ""(.*)"" mode searches for ""(.*)"" with the text null as its kind")]
    public async Task WhenSearchWithTextNull(string mode, string query)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: mode == "indexing");
        _fullTextReply = McpCodeClient.ReadContent(await client.CallAsync("search_code", new { query, kind = "null" }));
    }

    [When("MCP searches JSON navigation with these kind arguments:")]
    public async Task WhenSearchWithKindArguments(Table table)
    {
        await using var client = await StartCodeClientAsync();
        foreach (var row in table.Rows)
        {
            var arguments = new Dictionary<string, object?> { ["query"] = "index de navigation JSON" };
            if (row["argument"] != "omitted")
            {
                arguments["kind"] = JsonSerializer.Deserialize<string?>(row["argument"]);
            }

            var reply = McpCodeClient.ReadContent(await client.CallAsync("search_code", arguments));
            _kindSearchResults.Add((
                row["expected kinds"].Split(", ", StringSplitOptions.RemoveEmptyEntries),
                reply.GetProperty("symbols").EnumerateArray()
                    .Select(symbol => symbol.GetProperty("kind").GetString()!).Order(StringComparer.Ordinal).ToArray()));
        }
    }

    [Then("every search honors the intended kind filter")]
    public void ThenKindFilters()
    {
        Assert.NotEmpty(_kindSearchResults);
        foreach (var result in _kindSearchResults) Assert.Equal(result.ExpectedKinds, result.ActualKinds);
    }

    [Then(@"full-text search matches the unfiltered dashboard search for ""(.*)""")]
    public void ThenMatchesDashboardSearch(string query)
    {
        var reader = new SqliteIndexDashboardReader(Paths);
        var project = Assert.Single(reader.ListProjects(Token));
        var branch = Assert.Single(project.Branches);
        var expected = reader.SearchCode(project.Id, branch.Name, query, null, 10, Token);
        Assert.Equal(2, expected.Symbols.Count);
        var actual = _fullTextReply.GetProperty("symbols").EnumerateArray().ToArray();
        Assert.Equal(expected.Symbols.Select(symbol => symbol.QualifiedName),
            actual.Select(symbol => symbol.GetProperty("qualifiedName").GetString()));
        Assert.Equal(expected.Symbols.Select(symbol => symbol.Summary),
            actual.Select(symbol => symbol.GetProperty("summary").GetString()));
        Assert.Equal(expected.HasMore, _fullTextReply.GetProperty("hasMore").GetBoolean());
    }

    [When(@"default MCP searches indexed code for ""(.*)""")]
    public async Task WhenDefaultFullTextSearch(string query) =>
        await SearchWithMcpAsync(query, exposeIndexing: false);

    [When(@"MCP with indexing administration searches indexed code for ""(.*)""")]
    public async Task WhenAdministrativeFullTextSearch(string query) =>
        await SearchWithMcpAsync(query, exposeIndexing: true);

    [Then(@"full-text search returns method ""(.*)"" from ""(.*)""")]
    public void ThenFullTextSearchReturnsMethod(string expectedName, string path)
    {
        Assert.False(_fullTextError);
        var symbols = _fullTextReply.GetProperty("symbols").EnumerateArray().ToArray();
        var method = Assert.Single(symbols, s => s.GetProperty("name").GetString() == expectedName);
        Assert.Equal("method", method.GetProperty("kind").GetString());
        Assert.Equal(path, method.GetProperty("location").GetProperty("relativePath").GetString());
    }

    [Then("its response does not expose snapshot metadata")]
    public void ThenFullTextSearchHasNoSnapshot()
    {
        Assert.False(_fullTextReply.TryGetProperty("snapshot", out _));
        Assert.Equal(new[] { "hasMore", "symbols" },
            _fullTextReply.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Then("full-text search locations include matching absolute source paths")]
    public void ThenFullTextSearchHasAbsolutePaths() =>
        Assert.True(AssertAbsolutePaths(_fullTextReply) > 0);

    [Then("full-text search reports an error")]
    public void ThenFullTextSearchReportsError() => Assert.True(_fullTextError);

    [When("MCP searches all declarations for \"(.*)\"")]
    public async Task WhenSearchAllDeclarations(string query)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: false);
        var response = await client.CallAsync("search_code", new { query });
        _fullTextReply = McpCodeClient.ReadContent(response);
    }

    [Then("the first code result is type \"(.*)\" with description \"(.*)\"")]
    public void ThenFirstTypeDescription(string name, string summary)
    {
        var symbol = _fullTextReply.GetProperty("symbols")[0];
        Assert.Equal(name, symbol.GetProperty("name").GetString());
        Assert.Equal("class", symbol.GetProperty("kind").GetString());
        Assert.Equal(summary, symbol.GetProperty("summary").GetString());
    }

    private async Task SearchWithMcpAsync(string query, bool exposeIndexing)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: exposeIndexing);
        var response = await client.CallAsync("search_code", new { query, kind = "method" });
        _fullTextError = response.TryGetProperty("isError", out var error) && error.GetBoolean();
        _fullTextReply = _fullTextError ? response : McpCodeClient.ReadContent(response);
    }
}
