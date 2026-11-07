using System.Collections.Concurrent;
using System.Diagnostics;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Executable examples of syntax safety, debounce, and stale-location cleanup in watch mode.</summary>
[Binding]
public sealed class WatchRepositorySteps
{
    private SharedSummaryTestRepository? _repository;
    private RepositoryScope? _scope;
    private CancellationTokenSource? _cancellation;
    private Task? _running;
    private long _publishedSnapshot;
    private readonly ConcurrentQueue<string> _reports = new();

    [Given("a C# repository with a published code index")]
    public async Task GivenPublishedIndex()
    {
        _repository = new SharedSummaryTestRepository();
        await _repository.InitializeAsync();
        var indexed = await _repository.Indexer.ExecuteAsync(_repository.Root, cancellationToken: _repository.Token);
        _scope = indexed.Snapshot.Scope;
        _publishedSnapshot = indexed.Snapshot.SnapshotId;
    }

    [Given("I watch the repository with a short debounce")]
    public async Task GivenWatchRunning()
    {
        var repo = Repository;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(repo.Token);
        _running = new RepositoryIndexWatcher(repo.Indexer, new GitSummaryIntegration(), repo.Exporter, new YamlRepositoryConfigurationLoader())
            .RunAsync(repo.Root, _reports.Enqueue, _cancellation.Token,
                reconciliationInterval: TimeSpan.FromSeconds(10),
                debounceInterval: TimeSpan.FromMilliseconds(750));
        await Eventually(() => _reports.Any(message =>
            message.StartsWith("Index verified:", StringComparison.Ordinal)));
        _publishedSnapshot = repo.Store.GetSnapshot(Scope, repo.Token)!.SnapshotId;
    }

    [When("I save an incomplete C# declaration")]
    public void WhenSaveIncomplete() =>
        File.WriteAllText(SamplePath, "namespace Demo; public class Unfinished { public int Run( }");

    [Then("the watcher reports a syntax error without replacing the published index")]
    public async Task ThenInvalidFileDoesNotPublish()
    {
        await Eventually(() => _reports.Any(message =>
            message.Contains("Syntax error in 'Sample.cs'", StringComparison.Ordinal)));

        Assert.Equal(_publishedSnapshot, Repository.Store.GetSnapshot(Scope, Repository.Token)!.SnapshotId);
    }

    [When("I finish the C# declaration")]
    public void WhenFinishDeclaration() =>
        File.WriteAllText(SamplePath, "namespace Demo; public class Completed { public int Run() => 42; }");

    [Then("the watcher publishes the corrected declaration")]
    public async Task ThenPublishesCompleted()
    {
        await Eventually(() => Repository.Store.FindSymbols(Scope, "Completed", 10, Repository.Token)
            .Symbols.Count == 1);
        Assert.Empty(Repository.Store.FindSymbols(Scope, "Unfinished", 10, Repository.Token).Symbols);
    }

    [When("I save two valid C# edits within the quiet period")]
    public async Task WhenSaveRepeatedEdits()
    {
        File.WriteAllText(SamplePath, "namespace Demo; public class Intermediate { }");
        await Task.Delay(150, Repository.Token);
        File.WriteAllText(SamplePath, "namespace Demo; public class Settled { }");

        // No generation should be published immediately after either write.
        await Task.Delay(250, Repository.Token);
        Assert.Equal(_publishedSnapshot, Repository.Store.GetSnapshot(Scope, Repository.Token)!.SnapshotId);
    }

    [Then("the watcher publishes the final declaration but not the intermediate one")]
    public async Task ThenOnlyFinalDeclaration()
    {
        await Eventually(() => Repository.Store.FindSymbols(Scope, "Settled", 10, Repository.Token)
            .Symbols.Count == 1);
        Assert.Empty(Repository.Store.FindSymbols(Scope, "Intermediate", 10, Repository.Token).Symbols);
    }

    [When("I rename an indexed source file")]
    public void WhenRename() => File.Move(SamplePath, Path.Combine(Repository.Root, "Relocated.cs"));

    [Then("the watcher updates the declaration's source path")]
    public async Task ThenNewPath()
    {
        await Eventually(() => Repository.Store.FindSymbols(Scope, "Sample", 10, Repository.Token)
            .Symbols.SingleOrDefault()?.Location.RelativePath == "Relocated.cs");
    }

    [When("I delete the renamed source file")]
    public void WhenDelete() => File.Delete(Path.Combine(Repository.Root, "Relocated.cs"));

    [Then("the watcher removes its declaration")]
    public async Task ThenDeleted() =>
        await Eventually(() => Repository.Store.FindSymbols(Scope, "Sample", 10, Repository.Token)
            .Symbols.Count == 0);

    [Then("the watcher refreshes the shared summary for commit")]
    public async Task ThenRefreshesSharedSummaryForCommit()
    {
        await Eventually(() => File.Exists(Repository.SummaryPath)
            && File.ReadAllText(Repository.SummaryPath).Contains("Completed", StringComparison.Ordinal));
    }

    [AfterScenario]
    public async Task StopAsync()
    {
        if (_cancellation is not null)
        {
            await _cancellation.CancelAsync();
            if (_running is not null)
            {
                try
                {
                    await _running;
                }
                catch (OperationCanceledException)
                {
                    // Expected when the scenario stops the watcher.
                }
            }

            _cancellation.Dispose();
        }

        _repository?.Dispose();
    }

    private async Task Eventually(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15),
                "The watcher did not reach the expected state within 15 seconds.");
            await Task.Delay(100, Repository.Token);
        }
    }

    private SharedSummaryTestRepository Repository =>
        _repository ?? throw new InvalidOperationException("The test repository was not initialized.");

    private RepositoryScope Scope =>
        _scope ?? throw new InvalidOperationException("The repository index was not initialized.");

    private string SamplePath => Path.Combine(Repository.Root, "Sample.cs");
}
