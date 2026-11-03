using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Guards against Git inheriting stdin while the MCP transport is reading it.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private readonly List<string> _stdioMethodBodies = [];
    private JsonElement _externalIndexStatus;
    private JsonElement _externalSearch;
    private string _externalServerInfo = string.Empty;

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

    [When("an MCP client started outside the checkout inspects its repository")]
    public async Task WhenDesktopClientInspectsCheckout()
    {
        await using var client = await McpCodeClient.StartAsync(
            _root, Paths.Root, allowWrite: false, exposeIndexing: true,
            cancellationToken: Token, clientWorkingDirectory: _workspace);
        var serverInfo = await client.CallAsync("get_server_info", new { });
        _externalServerInfo = serverInfo.GetProperty("content")[0].GetProperty("text").GetString()!;
        _externalIndexStatus = McpCodeClient.ReadContent(
            await client.CallAsync("get_repository_index_status", new { }));
        _externalSearch = await client.CallAsync("find_symbols", new { name = "Worker" });
    }

    [Then("the MCP client finds Worker in the published index")]
    public void ThenDesktopClientFindsCachedSymbols()
    {
        var result = McpCodeClient.ReadContent(_externalSearch);
        Assert.Equal("Worker", Assert.Single(result.GetProperty("symbols").EnumerateArray())
            .GetProperty("name").GetString());
        Assert.NotEqual(JsonValueKind.Null, _externalIndexStatus.GetProperty("snapshot").ValueKind);
        Assert.True(_externalIndexStatus.GetProperty("indexDatabaseExists").GetBoolean());
        Assert.True(_externalIndexStatus.GetProperty("indexSnapshotExists").GetBoolean());
    }

    [Then("MCP reports the selected checkout, YAML and cache paths")]
    public async Task ThenDesktopClientHasCorrectPaths()
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var workingDirectory = _externalIndexStatus.GetProperty("workingDirectory").GetString()!;
        var workingState = await _stateProvider.GetStateAsync(workingDirectory, Token);

        Assert.Equal(state.Scope.RootPath, workingState.Scope.RootPath);
        Assert.Contains($"Repository root: {Path.GetFullPath(_root)}", _externalServerInfo);
        Assert.Equal(state.Scope.RootPath,
            _externalIndexStatus.GetProperty("checkout").GetProperty("scope").GetProperty("rootPath").GetString());
        Assert.Equal(Path.Combine(state.Scope.RootPath, "codesemantic4llm.yaml"),
            _externalIndexStatus.GetProperty("configurationPath").GetString());
        Assert.Equal(Paths.Root, _externalIndexStatus.GetProperty("indexRoot").GetString());
        Assert.Equal(Paths.GetDatabasePath(state.Scope.RootPath),
            _externalIndexStatus.GetProperty("indexDatabasePath").GetString());
    }

    [Then("MCP reports the missing database path")]
    public async Task ThenDesktopClientReportsMissingIndex()
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        // The MCP serializer may omit null properties entirely.
        Assert.False(_externalIndexStatus.GetProperty("indexSnapshotExists").GetBoolean());
        Assert.False(_externalIndexStatus.GetProperty("indexDatabaseExists").GetBoolean());
        Assert.Equal(Paths.GetDatabasePath(state.Scope.RootPath),
            _externalIndexStatus.GetProperty("indexDatabasePath").GetString());
        Assert.True(_externalSearch.GetProperty("isError").GetBoolean());
    }
}
