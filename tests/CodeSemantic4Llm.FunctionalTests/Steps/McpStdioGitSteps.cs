using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Guards against Git inheriting stdin while the MCP transport is reading it.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private readonly List<string> _stdioMethodBodies = [];

    [When(@"""(.*)"" MCP reads Run twice without closing the connection")]
    public async Task WhenReadMethodsOnOpenConnection(string mode)
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: mode == "indexing");

        // Each read checks Git state while the server waits for more protocol input.
        // Keep stdin open: closing it would hide the Windows inherited-pipe deadlock.
        for (var request = 0; request < 2; request++)
        {
            var result = await CodeCallAsync(client, "get_method_body", new
            {
                relativePath = "Worker.cs", name = "Run"
            });
            _stdioMethodBodies.Add(result.GetProperty("body").GetProperty("text").GetString()!);
        }
    }

    [Then(@"both MCP method reads return ""(.*)""")]
    public void ThenBothMethodReadsComplete(string expectedBody) =>
        Assert.Equal(new[] { expectedBody, expectedBody }, _stdioMethodBodies);
}
