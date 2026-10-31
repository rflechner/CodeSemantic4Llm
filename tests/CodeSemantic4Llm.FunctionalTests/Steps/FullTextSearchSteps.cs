using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>End-to-end full-text navigation with and without MCP indexing administration.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private JsonElement _fullTextReply;
    private bool _fullTextError;

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

    private async Task SearchWithMcpAsync(string query, bool exposeIndexing)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: exposeIndexing);
        var response = await client.CallAsync("search_code", new { query, kind = "method" });
        _fullTextError = response.TryGetProperty("isError", out var error) && error.GetBoolean();
        _fullTextReply = _fullTextError ? response : McpCodeClient.ReadContent(response);
    }
}
