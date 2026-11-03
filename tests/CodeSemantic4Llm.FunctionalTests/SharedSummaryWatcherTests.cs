using System.Collections.Concurrent;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class SharedSummaryWatcherTests
{
    [Fact]
    public async Task Startup_imports_shared_cache_then_events_refresh_FTS_and_sources()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        repo.UseFreshDatabase();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(repo.Token);
        var reports = new ConcurrentQueue<string>();
        var watcher = new RepositoryIndexWatcher(repo.Indexer, new GitSummaryIntegration());
        var running = watcher.RunAsync(repo.Root, reports.Enqueue, cancellation.Token, TimeSpan.FromSeconds(1));
        try
        {
            var scope = (await repo.State.GetStateAsync(repo.Root, repo.Token)).Scope;
            // A fresh database file can exist before its schema is initialized; wait for publication.
            await Eventually(() => reports.Any(report => report.StartsWith("Index verified:", StringComparison.Ordinal)), repo.Token);
            Assert.NotNull(repo.Store.GetSnapshot(scope, repo.Token));
            Assert.Equal(2, repo.Generator.Calls); // Startup reused the other developer's output.
            File.WriteAllText(repo.SummaryPath, File.ReadAllText(repo.SummaryPath).Replace("nebula", "watcherquasar"));
            await Eventually(() => repo.Store.SearchCode(scope, "watcherquasar", null, 10, repo.Token).Symbols.Count == 2, repo.Token);
            Assert.Equal(2, repo.Generator.Calls);
            File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "\nclass Added {}\n");
            await Eventually(() => repo.Store.FindSymbols(scope, "Added", 10, repo.Token).Symbols.Count == 1, repo.Token);
            Assert.True(repo.Generator.Calls > 2);
            Assert.DoesNotContain(reports, report => report.Contains("failed", StringComparison.Ordinal));
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Watcher_retries_after_a_failed_generation_and_keeps_previous_snapshot(bool providerTimeout)
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        var first = await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "\nclass Retry {}\n");
        repo.Generator.Fail = true;
        repo.Generator.Timeout = providerTimeout;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(repo.Token);
        var reports = new ConcurrentQueue<string>();
        var running = new RepositoryIndexWatcher(repo.Indexer, new GitSummaryIntegration())
            .RunAsync(repo.Root, reports.Enqueue, cancellation.Token, TimeSpan.FromMilliseconds(500));
        try
        {
            await Eventually(() => reports.Any(report => report.Contains("failed", StringComparison.Ordinal)), repo.Token);
            Assert.Equal(first.Snapshot.SnapshotId, repo.Store.GetSnapshot(first.Snapshot.Scope, repo.Token)!.SnapshotId);
            repo.Generator.Fail = false;
            await Eventually(() => repo.Store.FindSymbols(first.Snapshot.Scope, "Retry", 10, repo.Token).Symbols.Count == 1, repo.Token);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
    }

    private static async Task Eventually(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The watcher did not publish the expected state within 15 seconds.");
            await Task.Delay(100, cancellationToken);
        }
    }
}
