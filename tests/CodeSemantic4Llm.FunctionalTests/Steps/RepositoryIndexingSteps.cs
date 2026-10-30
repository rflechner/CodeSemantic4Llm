using System.Diagnostics;
using System.Text;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Tools;
using CodeSemantic4Llm.Persistence.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Indexes real temporary Git checkouts through Roslyn and SQLite, including rollback and branch isolation.</summary>
[Binding]
public sealed partial class RepositoryIndexingSteps
{
    private const string DefaultYaml = "version: 1\nanalyzers: [dotnet-csharp]\nfileExtensions:\n  dotnet-csharp: [.cs, .csharp]\nexclude: [vendor/**]\n";
    private const string SourceMarker = "SOURCE_BODY_MUST_NEVER_BE_STORED_37eabc";
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "CodeSemantic4Llm.IndexTests", Guid.NewGuid().ToString("N"));
    private readonly TrackingAnalyzer _analyzer = new();
    private readonly GitRepositoryStateProvider _stateProvider = new();
    private readonly List<RepositoryIndexingProgress> _progress = [];
    private string _root = "";
    private RepositoryIndexingResult? _result;
    private RepositoryIndexSnapshot? _previous;
    private Exception? _failure;
    private ConsoleCommandResult? _console;
    private CancellationTokenSource? _cancellation;
    private bool _readerObservedPrevious;
    private bool _cacheInsideCheckout;
    private RepositoryIndexStatus? _status;

    private IndexStoragePaths Paths => new(Path.Combine(_cacheInsideCheckout ? _root : _workspace, "cache"));
    private SqliteIndexStore Store => new(Paths);
    private string DatabasePath => Paths.GetDatabasePath(_root);

    [Given("a Git checkout configured for persistent indexing")]
    public async Task GivenCheckout()
    {
        _root = await CreateCheckoutAsync(Path.Combine(_workspace, "repo [index]"));
    }

    [Given("the indexing checkout contains these sources:")]
    public void GivenSources(Table table)
    {
        foreach (var row in table.Rows)
        {
            WriteSource(row["path"], row["source"]);
        }
    }

    [Given(@"the indexing checkout contains (\d+) C# files?")]
    public void GivenManyFiles(int count)
    {
        for (var index = 0; index < count; index++)
        {
            WriteSource($"src/File{index:D4}.cs", $"namespace Sample{index}; public class SharedType {{ public string GetValue() => \"{SourceMarker}\"; }}");
        }
    }

    [Given("source links in the indexing checkout point outside it")]
    public void GivenSourceLinks()
    {
        var outside = Path.Combine(_workspace, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "Outside.cs"), "public class OutsideType {}");
        File.CreateSymbolicLink(Path.Combine(_root, "Linked.cs"), Path.Combine(outside, "Outside.cs"));
        Directory.CreateSymbolicLink(Path.Combine(_root, "linked-directory"), outside);
    }

    [Given("the checkout has a completed index")]
    public async Task GivenCompletedIndex()
    {
        await WhenIndex();
        _previous = _result!.Snapshot;
    }

    [When("I index the checkout")]
    [When("I index the checkout again")]
    public async Task WhenIndex()
    {
        _progress.Clear();
        _analyzer.AnalysisCount = 0;
        _result = await CreateIndexer().ExecuteAsync(_root, new InlineProgress(_progress.Add), Token);
    }

    [When("I index the checkout from the console")]
    public async Task WhenIndexFromConsole()
    {
        var services = new ServiceCollection();
        services.AddSingleton(CreateIndexer());
        services.AddSingleton(Paths);
        var relativeRoot = Path.GetRelativePath(Directory.GetCurrentDirectory(), _root);
        _console = await ConsoleCommandRunner.RunAsync(["index", relativeRoot], services, Token);
    }

    [When("I attempt to index the checkout")]
    public async Task WhenAttemptIndex()
    {
        _failure = await Record.ExceptionAsync(WhenIndex);
    }

    [Given("I replace indexing source \"(.*)\" with \"(.*)\"")]
    [When("I replace indexing source \"(.*)\" with \"(.*)\"")]
    public void WhenReplaceSource(string path, string source) => WriteSource(path, source);

    [When("I delete indexing source \"(.*)\"")]
    public void WhenDeleteSource(string path) => File.Delete(Path.Combine(_root, path));

    [When("I rename indexing source \"(.*)\" to \"(.*)\"")]
    public void WhenRenameSource(string oldPath, string newPath) => File.Move(Path.Combine(_root, oldPath), Path.Combine(_root, newPath));

    [When("I switch the indexing checkout to a new branch \"(.*)\"")]
    public Task WhenSwitchBranch(string branch) => GitAsync(_root, "switch", "-c", branch);

    [When("I index a different checkout containing \"(.*)\"")]
    public async Task WhenIndexDifferentCheckout(string name)
    {
        var other = await CreateCheckoutAsync(Path.Combine(_workspace, "second"));
        File.WriteAllText(Path.Combine(other, "Other.cs"), $"public class {name} {{}}");
        await CreateIndexer().ExecuteAsync(other, cancellationToken: Token);
    }

    [Given("the next index run will encounter \"(.*)\"")]
    public void GivenFailure(string failure)
    {
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _analyzer.AfterAnalysis = async _ =>
        {
            switch (failure)
            {
                case "cancellation": _cancellation.Cancel(); break;
                case "analyzer failure": throw new IOException("Simulated analyzer failure.");
                case "source modification": WriteSource("First.cs", "public class ConcurrentType {}"); break;
                case "source deletion": File.Delete(Path.Combine(_root, "First.cs")); break;
                case "branch switch": await GitAsync(_root, "switch", "-c", "changed-during-index"); break;
                case "configuration change": File.AppendAllText(Path.Combine(_root, "codesemantic4llm.yaml"), "semanticIndexing: {language: fr}\n"); break;
                default: throw new ArgumentException($"Unknown test failure '{failure}'.");
            }
        };
    }

    [Given("a reader queries the index during the next analysis")]
    public void GivenConcurrentReader()
    {
        _analyzer.AfterAnalysis = _ =>
        {
            var observed = Store.FindSymbols(_previous!.Scope, "OldType", 20);
            _readerObservedPrevious = observed.Snapshot.SnapshotId == _previous.SnapshotId && observed.Symbols.Count == 1;
            return Task.CompletedTask;
        };
    }

    [When("the analyzer indexing version changes")]
    public void WhenAnalyzerVersionChanges() => _analyzer.Version = "2";

    [When("the indexing description language changes to \"(.*)\"")]
    public void WhenLanguageChanges(string language) =>
        File.WriteAllText(Path.Combine(_root, "codesemantic4llm.yaml"), DefaultYaml + $"semanticIndexing: {{language: {language}}}\n");

    [When("the indexing project name changes to \"(.*)\"")]
    public void WhenProjectNameChanges(string name) =>
        File.AppendAllText(Path.Combine(_root, "codesemantic4llm.yaml"), $"projectName: {name}\n");

    [Given("persistent indexing configuration is \"(.*)\"")]
    public void GivenInvalidConfiguration(string problem)
    {
        var path = Path.Combine(_root, "codesemantic4llm.yaml");

        switch (problem)
        {
            case "missing": File.Delete(path); break;
            case "unsupported analyzer": File.WriteAllText(path, "version: 1\nanalyzers: [typescript]\n"); break;
            case "invalid language": File.WriteAllText(path, DefaultYaml + "semanticIndexing: {language: english}\n"); break;
            default: throw new ArgumentException(problem);
        }
    }

    [When("I commit the indexing checkout")]
    public async Task WhenCommit()
    {
        await GitAsync(_root, "add", ".");
        await GitAsync(_root, "commit", "-m", "Update test sources");
    }

    [When("I request index command help")]
    public async Task WhenRequestHelp() => _console = await ConsoleCommandRunner.RunAsync(["index", "--help"], new ServiceCollection(), Token);

    [Given("the index schema version is newer than supported")]
    public void GivenNewerSchema() => ExecuteSql("PRAGMA user_version = 999;");

    [Given("the indexing target is \"(.*)\"")]
    public async Task GivenInvalidTarget(string target)
    {
        switch (target)
        {
            case "not Git":
                Directory.Delete(Path.Combine(_root, ".git"), recursive: true);
                break;
            case "subdirectory":
                _root = Directory.CreateDirectory(Path.Combine(_root, "nested")).FullName;
                File.WriteAllText(Path.Combine(_root, "codesemantic4llm.yaml"), DefaultYaml);
                break;
            case "detached HEAD":
                await GitAsync(_root, "checkout", "--detach");
                break;
            default:
                throw new ArgumentException(target);
        }
    }

    [Given("the indexing checkout is on an unborn branch")]
    public Task GivenUnbornBranch() => GitAsync(_root, "checkout", "--orphan", "unborn");

    [Given("the index cache is inside the checkout")]
    public void GivenCacheInsideCheckout() => _cacheInsideCheckout = true;

    [When("I read the saved index status")]
    public async Task WhenReadStatus() => _status = await CreateMcpTools().GetRepositoryIndexStatusAsync(Token);

    [When("indexing excludes \"(.*)\"")]
    public void WhenExclude(string path) => File.WriteAllText(Path.Combine(_root, "codesemantic4llm.yaml"),
        DefaultYaml.Replace("exclude: [vendor/**]", $"exclude: [vendor/**, \"{path}\"]", StringComparison.Ordinal));

    [Then("the status has no snapshot")]
    public void ThenNoSnapshot()
    {
        Assert.NotNull(_status);
        Assert.Null(_status.Snapshot);
    }

    [Then("the snapshot has no recorded revision")]
    public void ThenNoRevision() => Assert.Null(_result!.Snapshot.RecordedRevision);

    [Then(@"the index command succeeds with (\d+) indexed files")]
    public void ThenCommandSucceeds(int count)
    {
        Assert.NotNull(_console);
        Assert.Equal(0, _console.ExitCode);
        Assert.Empty(_console.Error);
        Assert.Contains($"Indexed files: {count};", _console.Output);
        Assert.Contains($"Index database: {DatabasePath}", _console.Output);
        Assert.True(File.Exists(DatabasePath));
        Assert.DoesNotContain("\u001b", _console.Output, StringComparison.Ordinal);
    }

    [Then(@"a new store reads a published snapshot with (\d+) files")]
    public async Task ThenSnapshotHasFiles(int count)
    {
        var state = await _stateProvider.GetStateAsync(_root, TestContext.Current.CancellationToken);
        var snapshot = Store.GetSnapshot(state.Scope);
        Assert.NotNull(snapshot);
        Assert.Equal(count, snapshot.FileCount);
        Assert.Equal(state.Revision, snapshot.RecordedRevision);
    }

    [Then("MCP finds declarations in the saved snapshot")]
    public async Task ThenMcpFindsDeclarations()
    {
        var tools = CreateMcpTools();
        var status = await tools.GetRepositoryIndexStatusAsync(Token);
        var result = await tools.FindSymbolsAsync("SharedType", 20, Token);
        Assert.NotNull(status.Snapshot);
        Assert.Equal(status.Snapshot.SnapshotId, result.Snapshot.SnapshotId);
        Assert.Equal(20, result.Symbols.Count);
        Assert.True(result.HasMore);
    }

    [Then("the cache contains derived metadata without source text")]
    public void ThenNoSourceCopies()
    {
        foreach (var file in Directory.EnumerateFiles(Paths.Root, "*", SearchOption.AllDirectories))
        {
            Assert.Contains(Path.GetFileName(file), new[] { "index.sqlite", "index.sqlite-wal", "index.sqlite-shm" });
            Assert.DoesNotContain(SourceMarker, Encoding.UTF8.GetString(File.ReadAllBytes(file)));
        }
    }

    [Then("the saved index does not contain \"(.*)\"")]
    public async Task ThenNoDeclaration(string name)
    {
        var state = await _stateProvider.GetStateAsync(_root, TestContext.Current.CancellationToken);
        Assert.Empty(Store.FindSymbols(state.Scope, name, 20).Symbols);
    }

    [Then("declaration \"(.*)\" is stored at \"(.*)\"")]
    public async Task ThenDeclarationLocation(string name, string path)
    {
        var state = await _stateProvider.GetStateAsync(_root, Token);
        var symbol = Assert.Single(Store.FindSymbols(state.Scope, name, 20).Symbols);
        Assert.Equal(path, symbol.Location.RelativePath);
        Assert.True(symbol.Location.Line > 0 && symbol.Location.Column > 0);
    }

    [Then("indexing progress reaches completion after publication")]
    public void ThenProgressCompletes()
    {
        Assert.Equal(RepositoryIndexingPhase.Discovering, _progress[0].Phase);
        Assert.Equal(RepositoryIndexingPhase.Completed, _progress[^1].Phase);
        Assert.Contains(_progress, progress => progress.Phase == RepositoryIndexingPhase.Verifying);
        Assert.Equal(_result!.Snapshot.FileCount, _progress[^1].CompletedFiles);
        Assert.Equal(_result.Snapshot.SnapshotId, Store.GetSnapshot(_result.Snapshot.Scope)!.SnapshotId);
    }

    [Then(@"the run analyzes (\d+) files and reuses (\d+) files")]
    public void ThenReuseCounts(int analyzed, int reused)
    {
        Assert.NotNull(_result);
        Assert.Equal(analyzed, _result.AnalyzedFiles);
        Assert.Equal(reused, _result.ReusedFiles);
        Assert.Equal(analyzed, _analyzer.AnalysisCount);
    }

    [Then("only one snapshot remains for the indexed branch")]
    public void ThenOnlyOneCurrentSnapshot() => ThenOnlyOneBranchSnapshot(_result!.Snapshot.Scope.BranchName);

    [Then("only one snapshot remains for branch \"(.*)\"")]
    public void ThenOnlyOneBranchSnapshot(string branch)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM snapshots WHERE branch = $branch;";
        command.Parameters.AddWithValue("$branch", branch);
        Assert.Equal(1L, command.ExecuteScalar());
    }

    [Then("branch \"(.*)\" retains declaration \"(.*)\"")]
    public void ThenBranchRetains(string branch, string name) =>
        Assert.Single(Store.FindSymbols(new RepositoryScope(_root, branch), name, 20).Symbols);

    [Then("the cache has separate databases for the two checkouts")]
    public void ThenSeparateDatabases() => Assert.Equal(2, Directory.GetFiles(Paths.Root, "index.sqlite", SearchOption.AllDirectories).Length);

    [Then("indexing fails without publishing a snapshot")]
    public void ThenRunFailed()
    {
        Assert.NotNull(_failure);
        Assert.DoesNotContain(_progress, progress => progress.Phase == RepositoryIndexingPhase.Completed);
    }

    [Then("the previous snapshot still contains \"(.*)\"")]
    public void ThenPreviousSnapshotSurvives(string name)
    {
        var result = Store.FindSymbols(_previous!.Scope, name, 20);
        Assert.Equal(_previous.SnapshotId, result.Snapshot.SnapshotId);
        Assert.Single(result.Symbols);
    }

    [Then("the reader observed the previous complete snapshot")]
    public void ThenReaderObservedPrevious() => Assert.True(_readerObservedPrevious);

    [Then("the published semantic language is \"(.*)\"")]
    public void ThenPublishedLanguage(string language) => Assert.Equal(language, _result!.Snapshot.SemanticLanguage);

    [Then("no index database was created")]
    public void ThenNoDatabase() => Assert.False(Directory.Exists(Paths.Root));

    [Then(@"querying ""(.*)"" with a limit of (\d+) returns (\d+) ordered results and indicates more")]
    public async Task ThenBoundedResults(string name, int limit, int count)
    {
        var result = await new FindSymbols(Store, _stateProvider).ExecuteAsync(_root, name, limit, Token);
        Assert.Equal(count, result.Symbols.Count);
        Assert.True(result.HasMore);
        Assert.Equal(new[] { "src/File0000.cs", "src/File0001.cs" }, result.Symbols.Select(symbol => symbol.Location.RelativePath));
    }

    [Then("a symbol query with a limit of 201 is rejected")]
    public Task ThenInvalidQueryLimit() => Assert.ThrowsAsync<ArgumentOutOfRangeException>(
        () => new FindSymbols(Store, _stateProvider).ExecuteAsync(_root, "SharedType", 201, Token));

    [Then("MCP rejects searches until the checkout is reindexed")]
    public async Task ThenMcpRejectsStaleHead()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateMcpTools().FindSymbolsAsync("SharedType", cancellationToken: Token));
        Assert.Contains("HEAD has changed", error.Message);
    }

    [Then("index help succeeds")]
    public void ThenHelpSucceeds()
    {
        Assert.Equal(0, _console!.ExitCode);
        Assert.Empty(_console.Error);
        Assert.Contains("index <repository-path>", _console.Output);
    }

    [Then("the newer schema version remains unchanged")]
    public void ThenSchemaUnchanged()
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(999L, command.ExecuteScalar());
    }

    [AfterScenario]
    public void Cleanup()
    {
        _cancellation?.Dispose();

        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    private CancellationToken Token => _cancellation?.Token ?? TestContext.Current.CancellationToken;

    private IndexRepository CreateIndexer() => new(_stateProvider, new YamlRepositoryConfigurationLoader(),
        new FileSystemRepositoryFileEnumerator(), new RepositorySourceReader(), Store, [_analyzer], [new CSharpReferenceAnalyzer()]);

    private RepositoryIndexTools CreateMcpTools()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CODESEMANTIC4LLM_REPOSITORY_ROOT"] = _root
        }).Build();
        return new RepositoryIndexTools(configuration, _stateProvider, Store,
            new FindSymbols(Store, _stateProvider), new GetTypeMembers(Store, _stateProvider),
            new FindUsages(Store, _stateProvider));
    }

    private void WriteSource(string path, string content)
    {
        var fullPath = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private async Task<string> CreateCheckoutAsync(string path)
    {
        Directory.CreateDirectory(path);
        await GitAsync(path, "init", "--initial-branch=main", "--template=");
        File.WriteAllText(Path.Combine(path, "codesemantic4llm.yaml"), DefaultYaml);
        await GitAsync(path, "add", ".");
        await GitAsync(path, "commit", "-m", "Create index test fixture");
        return (await _stateProvider.GetStateAsync(path, TestContext.Current.CancellationToken)).Scope.RootPath;
    }

    private async Task GitAsync(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach (var argument in new[] { "-C", root, "-c", "user.name=Index Tests", "-c", "user.email=index-tests@example.invalid",
                     "-c", "commit.gpgsign=false", "-c", $"core.hooksPath={Path.Combine(_workspace, "no-hooks")}", "-c", "core.fsmonitor=false" }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        await output;
        Assert.True(process.ExitCode == 0, await error);
    }

    private SqliteConnection OpenDatabase()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private void ExecuteSql(string sql)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed class InlineProgress(Action<RepositoryIndexingProgress> report) : IProgress<RepositoryIndexingProgress>
    {
        public void Report(RepositoryIndexingProgress value) => report(value);
    }

    private sealed class TrackingAnalyzer : ISourceAnalyzer
    {
        private readonly ISourceAnalyzer _inner = new CSharpSourceAnalyzer();
        public string AnalyzerId => _inner.AnalyzerId;
        public string LanguageId => _inner.LanguageId;
        public SourceFileExtensions DefaultFileExtensions => _inner.DefaultFileExtensions;
        public string IndexVersion => _inner.IndexVersion + Version;
        public string Version { get; set; } = "1";
        public int AnalysisCount { get; set; }
        public Func<SourceDocument, Task>? AfterAnalysis { get; set; }
        public bool Supports(string relativePath) => _inner.Supports(relativePath);

        public async Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(SourceDocument document, CancellationToken cancellationToken = default)
        {
            AnalysisCount++;
            var symbols = await _inner.AnalyzeAsync(document, cancellationToken);

            if (AfterAnalysis is not null)
            {
                await AfterAnalysis(document);
            }

            return symbols;
        }
    }
}
