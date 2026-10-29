using System.Diagnostics;
using System.Text.Json;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises repository selection through the real stdio server and CLI validation.</summary>
[Binding]
public sealed class ServeRepositorySteps
{
    private readonly string _workspace = Path.Combine(ResolveDirectoryPath(Path.GetTempPath()), "CodeSemantic4Llm.ServeTests", Guid.NewGuid().ToString("N"));
    private ConsoleCommandResult? _console;
    private JsonElement _preview;

    private string SelectedRoot => Path.Combine(_workspace, "selected project [cli]");
    private string EnvironmentRoot => Path.Combine(_workspace, "environment project");
    private string CacheRoot => Path.Combine(_workspace, "cache");

    [Given("two configured MCP project directories")]
    public void GivenProjectDirectories()
    {
        CreateProject(SelectedRoot, "fr", "Selected.cs");
        CreateProject(EnvironmentRoot, "en", "Environment.cs");
    }

    [When("I serve the selected project using an \"(.*)\" path with \"(.*)\" environment configuration")]
    public Task WhenServeProject(string pathKind, string environment)
    {
        var path = pathKind == "relative" ? Path.GetRelativePath(_workspace, SelectedRoot) : SelectedRoot;
        return PreviewThroughMcpAsync(["serve", path], environment == "conflicting" ? EnvironmentRoot : null);
    }

    [When("I start MCP using \"(.*)\" and the environment repository")]
    public Task WhenUseEnvironment(string invocation) =>
        PreviewThroughMcpAsync(invocation == "serve" ? ["serve"] : [], EnvironmentRoot);

    [When("I request serve command help")]
    public async Task WhenRequestHelp() =>
        _console = await ConsoleCommandRunner.RunAsync(["serve", "--help"], new ServiceCollection(), TestContext.Current.CancellationToken);

    [When("I invoke serve with \"(.*)\"")]
    public async Task WhenInvalidArguments(string problem)
    {
        Directory.CreateDirectory(_workspace);
        var file = Path.Combine(_workspace, "project.txt");
        File.WriteAllText(file, "This is a file, not a repository directory.");
        string[] arguments = problem switch
        {
            "empty path" => ["serve", ""],
            "whitespace path" => ["serve", "   "],
            "nonexistent path" => ["serve", Path.Combine(_workspace, "missing")],
            "file path" => ["serve", file],
            "extra argument" => ["serve", _workspace, "extra"],
            "unknown option" => ["serve", _workspace, "--unknown"],
            _ => throw new ArgumentException(problem)
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        _console = await ConsoleCommandRunner.RunAsync(arguments, new ServiceCollection(), timeout.Token);
    }

    [Then("MCP previews the project selected by the argument")]
    public void ThenArgumentSelected() => AssertPreview(SelectedRoot, "fr", "Selected.cs");

    [Then("MCP previews the project selected by the environment")]
    public void ThenEnvironmentSelected() => AssertPreview(EnvironmentRoot, "en", "Environment.cs");

    [Then("MCP startup creates no index cache")]
    public void ThenNoCache() => Assert.False(Directory.Exists(CacheRoot));

    [Then("serve help describes the repository argument and environment fallback")]
    public void ThenHelp()
    {
        Assert.NotNull(_console);
        Assert.Equal(0, _console.ExitCode);
        Assert.Empty(_console.Error);
        Assert.Contains("serve [repository-path]", _console.Output);
        Assert.Contains("CODESEMANTIC4LLM_REPOSITORY_ROOT", _console.Output);
    }

    [Then("serve reports an error containing \"(.*)\"")]
    public void ThenError(string message)
    {
        Assert.NotNull(_console);
        Assert.Equal(1, _console.ExitCode);
        Assert.Empty(_console.Output);
        Assert.Contains(message, _console.Error);
    }

    [AfterScenario]
    public void Cleanup()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private async Task PreviewThroughMcpAsync(string[] arguments, string? environmentRoot)
    {
        var start = CreateStartInfo(arguments, environmentRoot);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await SendAsync(process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"serve-test","version":"1.0"}}}
                """, timeout.Token);
            var initialization = await ReceiveAsync(process, 1, timeout.Token);
            Assert.True(initialization.TryGetProperty("serverInfo", out _));

            await SendAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", timeout.Token);
            await SendAsync(process,
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"preview_repository_index","arguments":{"maxFiles":10}}}""",
                timeout.Token);
            var result = await ReceiveAsync(process, 2, timeout.Token);
            Assert.False(result.TryGetProperty("isError", out var isError) && isError.GetBoolean(), result.ToString());
            using var content = JsonDocument.Parse(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            _preview = content.RootElement.Clone();
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }

    private ProcessStartInfo CreateStartInfo(string[] arguments, string? environmentRoot)
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

        if (environmentRoot is not null)
        {
            start.Environment["CODESEMANTIC4LLM_REPOSITORY_ROOT"] = environmentRoot;
        }

        return start;
    }

    private static async Task SendAsync(Process process, string message, CancellationToken cancellationToken)
    {
        await process.StandardInput.WriteLineAsync(message.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<JsonElement> ReceiveAsync(Process process, int id, CancellationToken cancellationToken)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        Assert.NotNull(line);
        using var response = JsonDocument.Parse(line);
        Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());
        Assert.False(response.RootElement.TryGetProperty("error", out _), line);
        return response.RootElement.GetProperty("result").Clone();
    }

    private void AssertPreview(string root, string language, string path)
    {
        Assert.Equal(root, _preview.GetProperty("repositoryRoot").GetString());
        Assert.Equal(language, _preview.GetProperty("semanticIndexingLanguage").GetString());
        Assert.Equal(Path.GetFileName(root), _preview.GetProperty("projectName").GetString());
        var file = Assert.Single(_preview.GetProperty("files").EnumerateArray());
        Assert.Equal(path, file.GetProperty("relativePath").GetString());
    }

    private static void CreateProject(string root, string language, string file)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "codesemantic4llm.yaml"),
            $"version: 1\nprojectName: '{Path.GetFileName(root)}'\nanalyzers: [dotnet-csharp]\nsemanticIndexing: {{language: {language}}}\n");
        File.WriteAllText(Path.Combine(root, file), "public class Example {}");
    }

    private static string ResolveDirectoryPath(string path)
    {
        // Child processes report a physical working directory, including macOS's /var -> /private/var link.
        var directory = new DirectoryInfo(path);
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);

        if (target is not null)
        {
            return ResolveDirectoryPath(target.FullName);
        }

        return directory.Parent is null
            ? directory.FullName
            : Path.Combine(ResolveDirectoryPath(directory.Parent.FullName), directory.Name);
    }
}
