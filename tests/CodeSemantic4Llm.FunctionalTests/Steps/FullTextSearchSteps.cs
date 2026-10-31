using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>End-to-end full-text navigation in both default and detailed MCP modes.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private JsonElement _fullTextReply;
    private bool _fullTextError;

    [When(@"default MCP searches indexed code for ""(.*)""")]
    public async Task WhenDefaultFullTextSearch(string query) =>
        await SearchWithMcpAsync(query, exposeIndexing: false);

    [When(@"detailed MCP searches indexed code for ""(.*)""")]
    public async Task WhenDetailedFullTextSearch(string query) =>
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
    public void ThenFullTextSearchHasNoSnapshot() =>
        Assert.False(_fullTextReply.TryGetProperty("snapshot", out _));

    [Then("its response includes published snapshot metadata")]
    public void ThenFullTextSearchHasSnapshot() =>
        Assert.True(_fullTextReply.GetProperty("snapshot").GetProperty("snapshotId").GetInt64() > 0);

    [Then("full-text search reports an error")]
    public void ThenFullTextSearchReportsError() => Assert.True(_fullTextError);

    private async Task SearchWithMcpAsync(string query, bool exposeIndexing)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: exposeIndexing);
        var response = await client.CallAsync("search_code", new { query, kind = "method" });
        _fullTextError = response.GetProperty("isError").GetBoolean();
        _fullTextReply = _fullTextError ? response : McpCodeClient.ReadContent(response);
    }
}
