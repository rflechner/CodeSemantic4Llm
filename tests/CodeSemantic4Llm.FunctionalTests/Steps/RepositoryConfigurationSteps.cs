using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
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

    [When("I preview source indexing")]
    public Task WhenIPreviewSourceIndexing() => PreviewAsync(50);

    [When(@"I preview source indexing with a limit of (\d+) file")]
    public Task WhenIPreviewSourceIndexingWithLimit(int limit) => PreviewAsync(limit);

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

    [Then("the effective exclusions include \"(.*)\"")]
    public void ThenEffectiveExclusionsInclude(string pattern)
    {
        Assert.NotNull(_preview);
        Assert.Contains(pattern, _preview.EffectiveExclusions);
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

    [AfterScenario]
    public void CleanUp()
    {
        if (Directory.Exists(_repositoryRoot))
        {
            Directory.Delete(_repositoryRoot, recursive: true);
        }
    }

    private async Task PreviewAsync(int maxFiles)
    {
        IRepositoryConfigurationLoader loader = new YamlRepositoryConfigurationLoader();
        IRepositoryFileEnumerator fileEnumerator = new FileSystemRepositoryFileEnumerator();
        ISourceAnalyzer[] analyzers = [new CSharpSourceAnalyzer()];
        var preview = new PreviewRepositoryIndex(loader, fileEnumerator, analyzers);

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
