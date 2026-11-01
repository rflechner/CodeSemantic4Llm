using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks project-scoped descriptions through real stdio tool discovery.</summary>
[Binding]
public sealed class ProjectToolDescriptionsSteps
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.ToolDescriptions", Guid.NewGuid().ToString("N"));
    private McpCodeClient? _client;
    private JsonElement _tools;
    private JsonElement _otherTools;

    private string Root => Path.Combine(_workspace, "checkout");
    private string CacheRoot => Path.Combine(_workspace, "cache");

    [Given("a tool discovery project named '(.*)'")]
    public void GivenNamedProject(string name) => WriteConfiguration(Root, name);

    [Given("a tool discovery project with \"(.*)\" configuration")]
    public void GivenConfiguration(string configuration)
    {
        Directory.CreateDirectory(Root);
        var yaml = configuration switch
        {
            "unnamed" => "version: 1\nanalyzers: [dotnet-csharp]\n",
            "null name" => "version: 1\nprojectName: null\nanalyzers: [dotnet-csharp]\n",
            "invalid" => "version: 1\nprojectName: [\n",
            "missing" => null,
            _ => throw new ArgumentException(configuration)
        };

        if (yaml is not null)
        {
            File.WriteAllText(Path.Combine(Root, "codesemantic4llm.yaml"), yaml);
        }
    }

    [When("I discover its MCP tools with editing \"(.*)\" and indexing \"(.*)\"")]
    public async Task WhenDiscoverTools(string editing, string indexing)
    {
        _client = await McpCodeClient.StartAsync(Root, CacheRoot, editing == "on", indexing == "on",
            TestContext.Current.CancellationToken);
        _tools = await _client.ListToolsAsync();
    }

    [When("I discover tools from this project and another project named \"(.*)\"")]
    public async Task WhenDiscoverBothProjects(string otherName)
    {
        await WhenDiscoverTools("off", "off");
        var otherRoot = Path.Combine(_workspace, "other-checkout");
        WriteConfiguration(otherRoot, otherName);
        await using var otherClient = await McpCodeClient.StartAsync(otherRoot, CacheRoot, false, false,
            TestContext.Current.CancellationToken);
        _otherTools = await otherClient.ListToolsAsync();
    }

    [When("I change the discovery project name to \"(.*)\" and refresh its tools")]
    public async Task WhenRenameProject(string name)
    {
        WriteConfiguration(Root, name);
        _tools = await _client!.ListToolsAsync();
    }

    [Then("every discovered tool description starts with \"(.*)\"")]
    [Then("every discovered tool description starts with '(.*)'")]
    public void ThenDescriptions(string prefix) => AssertDescriptions(_tools, prefix);

    [Then("the other server's descriptions start with \"(.*)\"")]
    public void ThenOtherDescriptions(string prefix) => AssertDescriptions(_otherTools, prefix);

    [Then("the file diagnostic description keeps its usage guidance")]
    public void ThenDiagnosticDescription()
    {
        var tool = _tools.GetProperty("tools").EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "get_file_diagnostics");
        Assert.EndsWith("Returns syntax errors and warnings in the whole C# file currently on disk. Use after editing or when a declaration cannot be selected. Follow nextOffset for more. Does not write or type-check.",
            tool.GetProperty("description").GetString());
    }

    [Then("discovery keeps editing \"(.*)\" and indexing \"(.*)\" exposure")]
    public void ThenExposure(string editing, string indexing)
    {
        var names = _tools.GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray();
        Assert.Equal(editing == "on", names.Contains("replace_method_body"));
        Assert.Equal(indexing == "on", names.Contains("get_repository_configuration"));
    }

    [Then("repeating discovery returns identical tools")]
    public async Task ThenRepeatDiscovery()
    {
        var repeated = await _client!.ListToolsAsync();
        Assert.Equal(_tools.GetRawText(), repeated.GetRawText());
    }

    [Then("tool discovery creates no index cache")]
    public void ThenNoIndexCache() => Assert.False(Directory.Exists(CacheRoot));

    [AfterScenario]
    public async Task Cleanup()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        TemporaryDirectory.TryRemove(_workspace);
    }

    private static void AssertDescriptions(JsonElement result, string prefix)
    {
        var tools = result.GetProperty("tools").EnumerateArray().ToArray();
        Assert.NotEmpty(tools);
        Assert.All(tools, tool => Assert.StartsWith(prefix, tool.GetProperty("description").GetString()));
    }

    private static void WriteConfiguration(string root, string name)
    {
        Directory.CreateDirectory(root);
        // A JSON string is also a YAML quoted scalar, including embedded punctuation.
        File.WriteAllText(Path.Combine(root, "codesemantic4llm.yaml"),
            $"version: 1\nprojectName: {JsonSerializer.Serialize(name)}\nanalyzers: [dotnet-csharp]\n");
    }
}
