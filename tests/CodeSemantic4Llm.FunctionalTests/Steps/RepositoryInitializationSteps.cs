using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises initialization against real directories and reads generated YAML through the preview pipeline.</summary>
[Binding]
public sealed class RepositoryInitializationSteps
{
    private const string OriginalContent = "# Preserve custom settings\r\nversion: 1\r\nanalyzers: [dotnet-csharp]\r\n";

    private readonly string _workspaceRoot = Path.Combine(
        Path.GetTempPath(), "CodeSemantic4Llm.FunctionalTests", Guid.NewGuid().ToString("N"));

    private ISourceAnalyzer[] _analyzers = [new CSharpSourceAnalyzer()];
    private int? _exitCode;
    private string? _output;
    private string? _error;

    private string RepositoryRoot => Path.Combine(_workspaceRoot, "target [repository]");
    private string ConfigurationPath => Path.Combine(RepositoryRoot, RepositoryIndexingConfiguration.FileName);
    private string ExternalConfigurationPath => Path.Combine(_workspaceRoot, "external.yaml");

    [Given("an existing target repository for initialization")]
    public void GivenExistingTargetRepository() => Directory.CreateDirectory(RepositoryRoot);

    [Given("the target repository already has a custom configuration")]
    public void GivenExistingConfiguration() => File.WriteAllText(ConfigurationPath, OriginalContent);

    [Given("the target configuration links to an external file")]
    public void GivenConfigurationLink()
    {
        SymbolicLinks.SkipWhenUnsupported();

        File.WriteAllText(ExternalConfigurationPath, OriginalContent);
        File.CreateSymbolicLink(ConfigurationPath, ExternalConfigurationPath);
    }

    [Given("the target configuration path is a directory")]
    public void GivenConfigurationDirectory() => Directory.CreateDirectory(ConfigurationPath);

    [Given("the target repository directory does not exist")]
    public void GivenMissingDirectory() => Directory.Delete(RepositoryRoot);

    [Given("the target repository path is a file")]
    public void GivenRepositoryPathIsFile()
    {
        Directory.Delete(RepositoryRoot);
        File.WriteAllText(RepositoryRoot, OriginalContent);
    }

    [Given("the default C# analyzer is unavailable for initialization")]
    public void GivenDefaultAnalyzerUnavailable() => _analyzers = [];

    [Given("a test-only analyzer \"(.*)\" is available for initialization")]
    public void GivenTestAnalyzerAvailable(string analyzerId) =>
        _analyzers = [.. _analyzers, new ConfigurationOnlyAnalyzer(analyzerId)];

    [When("^I initialize the target repository using an \"(absolute|relative)\" path$")]
    public Task WhenInitializeWithPath(string pathKind)
    {
        var path = pathKind == "absolute"
            ? RepositoryRoot
            : Path.GetRelativePath(Directory.GetCurrentDirectory(), RepositoryRoot);

        Assert.Equal(pathKind == "absolute", Path.IsPathRooted(path));
        return RunAsync([path]);
    }

    [When("I initialize the target repository with language \"(.*)\"")]
    public Task WhenInitializeWithLanguage(string language) => RunAsync([RepositoryRoot, "--lang", language]);

    [When("I run repository initialization with these arguments:")]
    public Task WhenInitializeWithArguments(Table table) => RunAsync(table.Rows
        .Select(row => row["argument"].Replace("{repository}", RepositoryRoot, StringComparison.Ordinal))
        .ToArray());

    [When("I run repository initialization without arguments")]
    public Task WhenInitializeWithoutArguments() => RunAsync([]);

    [When("I cancel repository initialization before it starts")]
    public Task WhenCancelInitialization() => RunAsync([RepositoryRoot], new CancellationToken(canceled: true));

    [Then("repository initialization succeeds")]
    public void ThenInitializationSucceeds()
    {
        Assert.Equal(0, _exitCode);
        Assert.Equal(string.Empty, _error);
        Assert.Contains(ConfigurationPath, _output);
        Assert.Equal([ConfigurationPath], Directory.GetFileSystemEntries(RepositoryRoot));
    }

    [Then("the generated configuration uses language \"(.*)\" and the default C# settings")]
    public async Task ThenGeneratedConfigurationUsesDefaults(string language)
    {
        var configuration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(RepositoryIndexingConfiguration.CurrentVersion, configuration.Version);
        Assert.Equal(["dotnet-csharp"], configuration.AnalyzerIds);
        Assert.Equal(language, configuration.SemanticIndexing.Language);
        Assert.Equal(RepositoryIndexingConfiguration.BuiltInExclusions, configuration.Exclusions);
        Assert.Empty(configuration.FileExtensions);
    }

    [Then("the generated configuration selects these analyzers in order:")]
    public async Task ThenGeneratedConfigurationSelectsAnalyzers(Table table)
    {
        var configuration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(table.Rows.Select(row => row["analyzer"]), configuration.AnalyzerIds);
        Assert.Empty(configuration.FileExtensions);
    }

    [Then("the generated configuration language is \"(.*)\"")]
    public async Task ThenGeneratedConfigurationLanguageIs(string language)
    {
        var configuration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(language, configuration.SemanticIndexing.Language);
    }

    [Then("the generated configuration names the project \"(.*)\"")]
    public async Task ThenGeneratedProjectName(string name)
    {
        var configuration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Equal(name, configuration.ProjectName);
        Assert.Contains("projectName:", await File.ReadAllTextAsync(ConfigurationPath, TestContext.Current.CancellationToken));
    }

    [Then("the generated configuration has no project name")]
    public async Task ThenGeneratedProjectNameIsOmitted()
    {
        var configuration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            RepositoryRoot, TestContext.Current.CancellationToken);

        Assert.Null(configuration.ProjectName);
        Assert.DoesNotContain("projectName:", await File.ReadAllTextAsync(ConfigurationPath, TestContext.Current.CancellationToken));
    }

    [Then("the initialized repository can be previewed with built-in exclusions")]
    public async Task ThenInitializedRepositoryCanBePreviewed()
    {
        foreach (var path in new[] { "src/Main.cs", "submodules/Library.cs", "src/bin/Generated.cs", "src/app.ts" })
        {
            var fullPath = Path.Combine(RepositoryRoot, path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, string.Empty);
        }

        var preview = await new PreviewRepositoryIndex(
            new YamlRepositoryConfigurationLoader(), new FileSystemRepositoryFileEnumerator(), _analyzers)
            .ExecuteAsync(RepositoryRoot, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["src/Main.cs", "submodules/Library.cs"], preview.Files.Select(file => file.RelativePath));
        Assert.Equal([".cs"], preview.EffectiveFileExtensions["dotnet-csharp"]);
    }

    [Then("repository initialization fails with an error containing \"(.*)\"")]
    public void ThenInitializationFails(string expected)
    {
        Assert.Equal(1, _exitCode);
        Assert.Equal(string.Empty, _output);
        Assert.Contains(expected, _error, StringComparison.OrdinalIgnoreCase);
    }

    [Then("initialization leaves no configuration or temporary files")]
    public void ThenNoFilesCreated() => Assert.Empty(Directory.GetFileSystemEntries(RepositoryRoot));

    [Then("the original target configuration is unchanged")]
    public void ThenConfigurationIsUnchanged() =>
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(OriginalContent), File.ReadAllBytes(ConfigurationPath));

    [Then("the external configuration and its symbolic link are unchanged")]
    public void ThenConfigurationLinkIsUnchanged()
    {
        Assert.Equal(OriginalContent, File.ReadAllText(ExternalConfigurationPath));
        Assert.Equal(ExternalConfigurationPath, new FileInfo(ConfigurationPath).LinkTarget);
        Assert.Equal([ConfigurationPath], Directory.GetFileSystemEntries(RepositoryRoot));
    }

    [Then("the directory at the configuration path is unchanged")]
    public void ThenConfigurationDirectoryIsUnchanged()
    {
        Assert.True(Directory.Exists(ConfigurationPath));
        Assert.Empty(Directory.GetFileSystemEntries(ConfigurationPath));
        Assert.Equal([ConfigurationPath], Directory.GetFileSystemEntries(RepositoryRoot));
    }

    [Then("the target repository directory still does not exist")]
    public void ThenRepositoryDirectoryStillMissing() => Assert.False(Path.Exists(RepositoryRoot));

    [Then("the file at the target repository path is unchanged")]
    public void ThenRepositoryFileIsUnchanged() => Assert.Equal(OriginalContent, File.ReadAllText(RepositoryRoot));

    [Then("initialization help succeeds")]
    public void ThenHelpSucceeds()
    {
        Assert.Equal(0, _exitCode);
        Assert.Equal(string.Empty, _error);
        Assert.Contains("USAGE:", _output);
        // Spectre inserts optional arguments between the executable and required path.
        Assert.Contains("codesemantic4llm", _output);
        Assert.Contains("init", _output);
        Assert.Contains("<repository-path>", _output);
        Assert.Contains("--analyzer", _output);
        Assert.Contains("repeat for multiple analyzers", _output);
        Assert.Contains("default: dotnet-csharp", _output);
        Assert.Contains("--lang", _output);
        Assert.Contains("--name", _output);
        Assert.Contains("en", _output);
    }

    [Then("repository initialization is canceled")]
    public void ThenInitializationIsCanceled()
    {
        Assert.Equal(130, _exitCode);
        Assert.Equal(string.Empty, _output);
    }

    [AfterScenario]
    public void CleanUp()
    {
        TemporaryDirectory.TryRemove(_workspaceRoot);
    }

    private async Task RunAsync(string[] arguments, CancellationToken? cancellationToken = null)
    {
        var services = new ServiceCollection();
        var initialize = new InitializeRepositoryConfiguration(new YamlRepositoryConfigurationWriter(), _analyzers);
        services.AddSingleton(initialize);

        var result = await ConsoleCommandRunner.RunAsync(
            ["init", .. arguments], services, cancellationToken ?? TestContext.Current.CancellationToken);
        _exitCode = result.ExitCode;
        _output = result.Output;
        _error = result.Error;
    }

    /// <summary>Registers an additional analyzer for selection tests without implementing another source language.</summary>
    private sealed class ConfigurationOnlyAnalyzer(string analyzerId) : ISourceAnalyzer
    {
        public string AnalyzerId => analyzerId;

        public string LanguageId => "test-language";

        public SourceFileExtensions DefaultFileExtensions { get; } = new([".test"]);

        public bool Supports(string relativePath) => DefaultFileExtensions.Matches(relativePath);

        public Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
            SourceDocument document,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Configuration initialization must not analyze source files.");
    }
}
