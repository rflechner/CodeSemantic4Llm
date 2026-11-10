using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Verifies the MCP search payload contains precise, bounded type references.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private JsonElement _typeUsageSearchReply;

    [When(@"MCP ""(.*)"" searches for (type|method) ""(.*)""")]
    public async Task WhenMcpSearchesForTypeOrMethod(string tool, string kind, string name)
    {
        await using var client = await StartCodeClientAsync();
        object arguments = tool switch
        {
            "find_symbols" => new { name },
            "search_code" => new { query = name, kind = kind == "type" ? "class" : "method" },
            _ => throw new ArgumentException($"Unknown MCP search tool '{tool}'.")
        };

        _typeUsageSearchReply = McpCodeClient.ReadContent(await client.CallAsync(tool, arguments));
    }

    [Then(@"MCP search returns (\d+) references to ""(.*)"" in ""(.*)""")]
    public void ThenSearchIncludesTypeUsages(int count, string name, string path)
    {
        var preview = FindTypeUsagePreview(name);
        var usages = preview.GetProperty("usages").EnumerateArray().ToArray();
        Assert.Equal(count, usages.Length);
        Assert.All(usages, usage =>
        {
            var location = usage.GetProperty("relativePath").GetString();
            Assert.Equal(path, location);
            Assert.True(usage.GetProperty("line").GetInt32() > 0);
            Assert.True(usage.GetProperty("column").GetInt32() > 0);
        });
    }

    [Then("the MCP type usage preview is available and complete")]
    public void ThenTypePreviewIsComplete()
    {
        var preview = Assert.Single(_typeUsageSearchReply.GetProperty("typeUsages").EnumerateArray());
        Assert.True(preview.GetProperty("isAvailable").GetBoolean());
        Assert.False(preview.GetProperty("hasMore").GetBoolean());
    }

    [Then("the MCP type usage preview has more results")]
    public void ThenTypePreviewHasMore()
    {
        var preview = Assert.Single(_typeUsageSearchReply.GetProperty("typeUsages").EnumerateArray());
        Assert.True(preview.GetProperty("isAvailable").GetBoolean());
        Assert.True(preview.GetProperty("hasMore").GetBoolean());
    }

    [Then("the MCP type usage preview is unavailable")]
    public void ThenTypePreviewIsUnavailable()
    {
        var preview = Assert.Single(_typeUsageSearchReply.GetProperty("typeUsages").EnumerateArray());
        Assert.False(preview.GetProperty("isAvailable").GetBoolean());
        Assert.Empty(preview.GetProperty("usages").EnumerateArray());
        Assert.False(preview.GetProperty("hasMore").GetBoolean());
    }

    [Then("MCP search does not contain type usage previews")]
    public void ThenMcpMethodSearchIsCompact()
    {
        Assert.False(_typeUsageSearchReply.TryGetProperty("typeUsages", out _));
        Assert.Equal(new[] { "hasMore", "symbols" },
            _typeUsageSearchReply.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Then("the MCP enriched search hides snapshot metadata")]
    public void ThenEnrichedSearchHidesIndexDetails()
    {
        Assert.False(_typeUsageSearchReply.TryGetProperty("snapshot", out _));
        Assert.DoesNotContain("\"snapshotId\"", _typeUsageSearchReply.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"recordedRevision\"", _typeUsageSearchReply.GetRawText(), StringComparison.Ordinal);
    }

    [Then("both Worker types have separate usage previews")]
    public void ThenTypePreviewsPreserveExactIdentity()
    {
        var symbols = _typeUsageSearchReply.GetProperty("symbols").EnumerateArray().ToArray();
        var previews = _typeUsageSearchReply.GetProperty("typeUsages").EnumerateArray().ToArray();
        Assert.Equal(2, symbols.Length);
        Assert.Equal(2, previews.Length);

        foreach (var symbol in symbols)
        {
            var definition = symbol.GetProperty("location");
            var preview = Assert.Single(previews, entry =>
            {
                var location = entry.GetProperty("typeLocation");
                return location.GetProperty("relativePath").GetString() == definition.GetProperty("relativePath").GetString()
                    && location.GetProperty("line").GetInt32() == definition.GetProperty("line").GetInt32()
                    && location.GetProperty("column").GetInt32() == definition.GetProperty("column").GetInt32();
            });
            Assert.True(preview.GetProperty("isAvailable").GetBoolean());
            Assert.False(preview.GetProperty("hasMore").GetBoolean());
            var usage = Assert.Single(preview.GetProperty("usages").EnumerateArray());
            Assert.Equal(definition.GetProperty("relativePath").GetString(),
                usage.GetProperty("relativePath").GetString());
        }
    }

    private JsonElement FindTypeUsagePreview(string name)
    {
        var symbol = Assert.Single(_typeUsageSearchReply.GetProperty("symbols").EnumerateArray(),
            item => item.GetProperty("name").GetString() == name && item.GetProperty("kind").GetString() == "class");
        var location = symbol.GetProperty("location");

        return Assert.Single(_typeUsageSearchReply.GetProperty("typeUsages").EnumerateArray(),
            entry =>
            {
                var target = entry.GetProperty("typeLocation");
                return target.GetProperty("relativePath").GetString() == location.GetProperty("relativePath").GetString()
                    && target.GetProperty("line").GetInt32() == location.GetProperty("line").GetInt32()
                    && target.GetProperty("column").GetInt32() == location.GetProperty("column").GetInt32();
            });
    }
}
