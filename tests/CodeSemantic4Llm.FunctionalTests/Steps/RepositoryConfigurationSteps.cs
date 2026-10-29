using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises YAML parsing, analyzer selection and actual filesystem filtering together.</summary>
[Binding]
public sealed class RepositoryConfigurationSteps
{
    private readonly string _repositoryRoot = Path.Combine(
        Path.GetTempPath(),
        "CodeSemantic4Llm.FunctionalTests",
        Guid.NewGuid().ToString("N"));

    private RepositoryIndexingPreview? _preview;
    private Exception? _error;
    private int? _consoleExitCode;
    private string? _consoleOutput;
    private string? _consoleError;

    public RepositoryConfigurationSteps()
    {
        Directory.CreateDirectory(_repositoryRoot);
    }

    [Given("the repository configuration is:")]
    public async Task GivenRepositoryConfigurationIs(string yaml)
    {
        await File.WriteAllTextAsync(
            Path.Combine(_repositoryRoot, "codesemantic4llm.yaml"), yaml);
    }

    [Given("the repository has no configuration file")]
    public void GivenRepositoryHasNoConfigurationFile()
    {
        Assert.False(File.Exists(Path.Combine(_repositoryRoot, "codesemantic4llm.yaml")));
    }

    [Given("the repository contains these source files:")]
    public void GivenRepositoryContainsSourceFiles(Table table)
    {
        foreach (var row in table.Rows)
        {
            var fullPath = Path.Combine(_repositoryRoot, row["path"].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, "namespace Test; public sealed class Example { }");
        }
    }

    [Given("a directory link \"(.*)\" points outside the repository")]
    public void GivenDirectoryLinkPointsOutsideRepository(string relativePath)
    {
        var externalRoot = CreateExternalSourceDirectory();
        Directory.CreateSymbolicLink(Path.Combine(_repositoryRoot, relativePath), externalRoot);
    }

    [Given("a directory link \"(.*)\" points to the repository root")]
    public void GivenDirectoryLinkPointsToRepositoryRoot(string relativePath)
    {
        Directory.CreateSymbolicLink(Path.Combine(_repositoryRoot, relativePath), _repositoryRoot);
    }

    [Given("a file link \"(.*)\" points outside the repository")]
    public void GivenFileLinkPointsOutsideRepository(string relativePath)
    {
        var externalRoot = CreateExternalSourceDirectory();
        File.CreateSymbolicLink(
            Path.Combine(_repositoryRoot, relativePath), Path.Combine(externalRoot, "External.cs"));
    }

    [When("I preview source indexing")]
    public Task WhenIPreviewSourceIndexing() => PreviewAsync(50);

    [When(@"I preview source indexing with a limit of (\d+) file")]
    public Task WhenIPreviewSourceIndexingWithLimit(int limit) => PreviewAsync(limit);

    [When("I preview source indexing from the console")]
    public Task WhenIPreviewFromConsole() => PreviewFromConsoleAsync([_repositoryRoot]);

    [When("I preview source indexing from the console with a limit of \"(.*)\"")]
    public Task WhenIPreviewFromConsoleWithLimit(string limit) =>
        PreviewFromConsoleAsync([_repositoryRoot, "--max-files", limit]);

    [When("I request console preview help")]
    public Task WhenIRequestConsolePreviewHelp() => PreviewFromConsoleAsync(["--help"]);

    [When("I run the console preview without a repository path")]
    public Task WhenIPreviewWithoutRepositoryPath() => PreviewFromConsoleAsync([]);

    [Then("the console preview succeeds")]
    public void ThenConsolePreviewSucceeds()
    {
        Assert.Equal(0, _consoleExitCode);
        Assert.Equal(string.Empty, _consoleError);
    }

    [Then("the console lists these files:")]
    public void ThenConsoleListsFiles(Table table)
    {
        Assert.NotNull(_consoleOutput);

        var expected = table.Rows.Select(row => $"{row["analyzer"]}  {row["path"]}");
        var actual = _consoleOutput.Split(Environment.NewLine)
            .Where(line => line.StartsWith("dotnet-csharp  ", StringComparison.Ordinal));

        Assert.Equal(expected, actual);
    }

    [Then("the console output contains \"(.*)\"")]
    public void ThenConsoleOutputContains(string expected)
    {
        Assert.NotNull(_consoleOutput);
        Assert.Contains(expected, _consoleOutput, StringComparison.Ordinal);
    }

    [Then("the console preview fails with an error containing \"(.*)\"")]
    public void ThenConsolePreviewFails(string expected)
    {
        Assert.Equal(1, _consoleExitCode);
        Assert.Equal(string.Empty, _consoleOutput);
        Assert.NotNull(_consoleError);
        Assert.Contains(expected, _consoleError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Usage: preview", _consoleError, StringComparison.Ordinal);
    }

    [Then("the indexable files are:")]
    public void ThenIndexableFilesAre(Table table)
    {
        Assert.Null(_error);
        Assert.NotNull(_preview);

        var actual = _preview.Files
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .Select(item => (item.RelativePath, item.AnalyzerId))
            .ToArray();

        var expected = table.Rows
            .Select(row => (Path: row["path"].Trim(), Analyzer: row["analyzer"].Trim()))
            .OrderBy(item => item.Path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Then("the indexable files are returned in this order:")]
    public void ThenIndexableFilesAreReturnedInOrder(Table table)
    {
        Assert.Null(_error);
        Assert.NotNull(_preview);

        var expected = table.Rows.Select(row => (row["path"], row["analyzer"]));
        var actual = _preview.Files.Select(file => (file.RelativePath, file.AnalyzerId));

        Assert.Equal(expected, actual);
    }

    [Then("the effective extensions for \"(.*)\" are:")]
    public void ThenEffectiveExtensionsAre(string analyzerId, Table table)
    {
        Assert.Null(_error);
        Assert.NotNull(_preview);
        Assert.True(_preview.EffectiveFileExtensions.TryGetValue(analyzerId, out var extensions));

        Assert.Equal(table.Rows.Select(row => row["extension"]), extensions);
    }

    [Then("the effective exclusions include \"(.*)\"")]
    public void ThenEffectiveExclusionsInclude(string pattern)
    {
        Assert.NotNull(_preview);
        Assert.Contains(pattern, _preview.EffectiveExclusions);
    }

    [Then("the semantic indexing language is \"(.*)\"")]
    public void ThenSemanticIndexingLanguageIs(string expected)
    {
        Assert.Null(_error);
        Assert.NotNull(_preview);
        Assert.Equal(expected, _preview.SemanticIndexingLanguage);
    }

    [Then("the preview fails with an error containing \"(.*)\"")]
    public void ThenPreviewFailsWithErrorContaining(string expected)
    {
        Assert.NotNull(_error);
        Assert.Contains(expected, _error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Then(@"the preview contains (\d+) file")]
    public void ThenPreviewContainsFiles(int count)
    {
        Assert.NotNull(_preview);
        Assert.Equal(count, _preview.Files.Count);
    }

    [Then("the preview indicates that more files are available")]
    public void ThenPreviewIsTruncated()
    {
        Assert.NotNull(_preview);
        Assert.True(_preview.HasMore);
    }

    [Then("the preview indicates that all matching files are included")]
    public void ThenPreviewIsComplete()
    {
        Assert.Null(_error);
        Assert.NotNull(_preview);
        Assert.False(_preview.HasMore);
    }

    [AfterScenario]
    public void CleanUp()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }

        var externalRoot = _repositoryRoot + "-external";

        if (Directory.Exists(externalRoot))
        {
            Directory.Delete(externalRoot, recursive: true);
        }
    }

    private string CreateExternalSourceDirectory()
    {
        var externalRoot = _repositoryRoot + "-external";
        Directory.CreateDirectory(externalRoot);
        File.WriteAllText(Path.Combine(externalRoot, "External.cs"), "public class External { }");

        return externalRoot;
    }

    private static PreviewRepositoryIndex CreatePreviewUseCase()
    {
        IRepositoryConfigurationLoader loader = new YamlRepositoryConfigurationLoader();
        IRepositoryFileEnumerator fileEnumerator = new FileSystemRepositoryFileEnumerator();
        ISourceAnalyzer[] analyzers = [new CSharpSourceAnalyzer()];

        return new PreviewRepositoryIndex(loader, fileEnumerator, analyzers);
    }

    private async Task PreviewFromConsoleAsync(string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var command = new RepositoryPreviewCommand(CreatePreviewUseCase());

        _consoleExitCode = await command.ExecuteAsync(arguments, output, error, TestContext.Current.CancellationToken);
        _consoleOutput = output.ToString();
        _consoleError = error.ToString();
    }

    private async Task PreviewAsync(int maxFiles)
    {
        var preview = CreatePreviewUseCase();

        try
        {
            _preview = await preview.ExecuteAsync(_repositoryRoot, maxFiles);
        }
        catch (Exception exception)
        {
            _error = exception;
        }
    }
}
