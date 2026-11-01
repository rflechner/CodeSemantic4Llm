using System.Diagnostics;
using System.Text.Json;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>
/// Exercises the repository-local MCP tool selection through the real stdio server, the CLI
/// initializer and the configuration loader.
/// </summary>
[Binding]
public sealed class McpToolSelectionSteps : IDisposable
{
    private const string SourceFileName = "Example.cs";

    private readonly string _workspace = Path.Combine(
        Path.GetTempPath(), "CodeSemantic4Llm.ToolSelectionTests", Guid.NewGuid().ToString("N"));

    private Process? _server;
    private int _requestId;
    private string[] _advertisedTools = [];
    private JsonElement _callResponse;
    private ConsoleCommandResult? _console;
    private string _generatedConfiguration = "";

    private string ProjectRoot => Path.Combine(_workspace, "checkout");

    private string CacheRoot => Path.Combine(_workspace, "cache");

    [Given("a project that selects every tool")]
    public void GivenProjectSelectingEveryTool() => CreateProject(toolsSection: null);

    [Given("a project that selects these tools:")]
    public void GivenProjectSelectingTools(Table table)
    {
        var selected = table.Rows.Select(row => $"  - {row["tool"]}");
        CreateProject(string.Join(Environment.NewLine, ["tools:", .. selected]));
    }

    [Given("a project with this tools section:")]
    public void GivenProjectWithToolsSection(string section) => CreateProject(section);

    [When("I initialize a project from the console")]
    public async Task WhenInitializeFromConsole()
    {
        Directory.CreateDirectory(ProjectRoot);
        var services = new ServiceCollection();
        services.AddSingleton(new InitializeRepositoryConfiguration(
            new YamlRepositoryConfigurationWriter(), [new CSharpSourceAnalyzer()]));
        _console = await ConsoleCommandRunner.RunAsync(
            ["init", ProjectRoot], services, TestContext.Current.CancellationToken);

        Assert.Equal(0, _console.ExitCode);
        _generatedConfiguration = await File.ReadAllTextAsync(
            Path.Combine(ProjectRoot, RepositoryIndexingConfiguration.FileName),
            TestContext.Current.CancellationToken);
    }

    [When("I preview it from the console")]
    public async Task WhenPreviewFromConsole()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new PreviewRepositoryIndex(
            new YamlRepositoryConfigurationLoader(),
            new FileSystemRepositoryFileEnumerator(),
            [new CSharpSourceAnalyzer()]));
        _console = await ConsoleCommandRunner.RunAsync(
            ["preview", ProjectRoot], services, TestContext.Current.CancellationToken);
    }

    [When("I serve it with editing \"(.*)\" and indexing \"(.*)\"")]
    public async Task WhenServeProject(string editing, string indexing)
    {
        List<string> arguments = ["serve", ProjectRoot];

        if (editing == "on")
        {
            arguments.Add("--allow-write");
        }

        if (indexing == "on")
        {
            arguments.Add("--expose-indexing");
        }

        _server = Process.Start(CreateStartInfo(arguments))!;
        await SendAsync(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"tool-selection-test","version":"1.0"}}}
            """);
        var initialization = await ReceiveAsync(1);
        Assert.True(initialization.GetProperty("result").TryGetProperty("serverInfo", out _));
        await SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
        _requestId = 1;
    }

    [When("I list its MCP tools")]
    public async Task WhenListTools()
    {
        var id = ++_requestId;
        await SendAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = "tools/list" }));
        var response = await ReceiveAsync(id);

        _advertisedTools = response.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!)
            .ToArray();
    }

    [When("I call the \"(.*)\" tool")]
    public async Task WhenCallTool(string toolName)
    {
        var id = ++_requestId;
        object arguments = toolName == "get_file_diagnostics"
            ? new { relativePath = SourceFileName }
            : new { };

        await SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call",
            @params = new { name = toolName, arguments }
        }));
        _callResponse = await ReceiveAsync(id);
    }

    [Then("the generated configuration selects every tool")]
    public void ThenGeneratedConfigurationSelectsEveryTool()
    {
        var selected = _generatedConfiguration.Split(Environment.NewLine)
            .Where(line => line.StartsWith("  - ", StringComparison.Ordinal))
            .Select(line => line[4..])
            .Where(value => McpToolCatalog.Find(value) is not null)
            .ToArray();

        Assert.Equal(McpToolCatalog.AllNames, selected);
    }

    [Then("the generated configuration explains how to hide a tool")]
    public void ThenGeneratedConfigurationExplainsHiding()
    {
        Assert.Contains("Comment out a line to hide that tool.",
            _generatedConfiguration, StringComparison.Ordinal);
        Assert.Contains("serve --allow-write", _generatedConfiguration, StringComparison.Ordinal);
        Assert.Contains("serve --expose-indexing", _generatedConfiguration, StringComparison.Ordinal);
    }

    [Then("MCP advertises exactly every tool of the catalog")]
    public void ThenAdvertisesEveryCatalogTool() => AssertAdvertised(McpToolCatalog.AllNames);

    [Then("MCP advertises exactly every read-only tool of the catalog")]
    public void ThenAdvertisesEveryReadOnlyTool() =>
        AssertAdvertised(McpToolCatalog.All
            .Where(tool => tool.Capability == McpToolCapability.Code)
            .Select(tool => tool.Name)
            .ToArray());

    [Then("MCP advertises exactly these tools:")]
    public void ThenAdvertisesExactly(Table table) =>
        AssertAdvertised(table.Rows.Select(row => row["tool"]).ToArray());

    [Then("the tool call succeeds")]
    public void ThenToolCallSucceeds()
    {
        Assert.False(_callResponse.TryGetProperty("error", out var error), error.ToString());
        Assert.False(_callResponse.GetProperty("result").TryGetProperty("isError", out var isError)
            && isError.GetBoolean(), _callResponse.ToString());
    }

    [Then("the tool call is refused with \"(.*)\"")]
    public void ThenToolCallRefused(string expected) =>
        Assert.Contains(expected, DescribeRefusal(), StringComparison.Ordinal);

    /// <summary>
    /// A tool the startup capabilities never registered is refused as unknown, while a registered
    /// tool this repository deselected is refused by name. Both refusals identify the tool.
    /// </summary>
    [Then("calling \"(.*)\" is refused")]
    public async Task ThenCallingToolIsRefused(string toolName)
    {
        await WhenCallTool(toolName);
        Assert.Contains(toolName, DescribeRefusal(), StringComparison.Ordinal);
    }

    [Then("the console reports an error containing \"(.*)\"")]
    public void ThenConsoleReportsError(string expected)
    {
        Assert.NotNull(_console);
        Assert.Equal(1, _console.ExitCode);
        Assert.Contains(expected, _console.Error, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_server is not null)
        {
            if (!_server.HasExited)
            {
                _server.Kill(entireProcessTree: true);
            }

            _server.WaitForExit();
            _server.Dispose();
        }

        TemporaryDirectory.TryRemove(_workspace);
    }

    /// <summary>A refusal may arrive as a protocol error or as an error tool result.</summary>
    private string DescribeRefusal()
    {
        if (_callResponse.TryGetProperty("error", out var error))
        {
            return error.GetProperty("message").GetString() ?? "";
        }

        var result = _callResponse.GetProperty("result");
        Assert.True(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(),
            $"The call was not refused: {_callResponse}");

        return result.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private void AssertAdvertised(IReadOnlyList<string> expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), _advertisedTools.Order(StringComparer.Ordinal));

    private void CreateProject(string? toolsSection)
    {
        Directory.CreateDirectory(ProjectRoot);
        string[] lines =
        [
            "version: 1",
            "projectName: ToolSelection",
            "analyzers: [dotnet-csharp]",
            .. toolsSection is null ? Array.Empty<string>() : [toolsSection]
        ];

        File.WriteAllText(
            Path.Combine(ProjectRoot, RepositoryIndexingConfiguration.FileName),
            string.Join(Environment.NewLine, lines) + Environment.NewLine);
        File.WriteAllText(Path.Combine(ProjectRoot, SourceFileName), "public sealed class Example { }");
    }

    private ProcessStartInfo CreateStartInfo(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = _workspace
        };
        start.ArgumentList.Add(typeof(StartMcpServerCommand).Assembly.Location);

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment.Remove("CODESEMANTIC4LLM_REPOSITORY_ROOT");
        start.Environment["CODESEMANTIC4LLM_INDEX_ROOT"] = CacheRoot;

        return start;
    }

    private async Task SendAsync(string message)
    {
        using var timeout = CreateTimeout();
        await _server!.StandardInput.WriteLineAsync(message.AsMemory(), timeout.Token);
        await _server.StandardInput.FlushAsync(timeout.Token);
    }

    private async Task<JsonElement> ReceiveAsync(int id)
    {
        using var timeout = CreateTimeout();
        var line = await _server!.StandardOutput.ReadLineAsync(timeout.Token);
        Assert.NotNull(line);

        using var response = JsonDocument.Parse(line);
        Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());

        return response.RootElement.Clone();
    }

    private static CancellationTokenSource CreateTimeout()
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        return timeout;
    }
}
