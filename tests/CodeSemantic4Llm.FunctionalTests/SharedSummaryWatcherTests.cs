using System.Collections.Concurrent;
using CodeSemantic4Llm.Configuration.Yaml;
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
        var watcher = new RepositoryIndexWatcher(repo.Indexer, new GitSummaryIntegration(), repo.Exporter, new YamlRepositoryConfigurationLoader());
        var running = watcher.RunAsync(repo.Root, reports.Enqueue, cancellation.Token, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
        try
        {
            var scope = (await repo.State.GetStateAsync(repo.Root, repo.Token)).Scope;
            // A fresh database file can exist before its schema is initialized; wait for publication.
            await Eventually(() => reports.Any(report => report.StartsWith("Index verified:", StringComparison.Ordinal)), repo.Token, reports);
            Assert.NotNull(repo.Store.GetSnapshot(scope, repo.Token));
            Assert.Equal(2, repo.Generator.Calls); // Startup reused the other developer's output.
            File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "\npublic class WatcherQuasar {}\n");
            await Eventually(() => repo.Store.FindSymbols(scope, "WatcherQuasar", 10, repo.Token).Symbols.Count == 1, repo.Token, reports);
            Assert.True(repo.Generator.Calls > 2);
            File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "\nclass Added {}\n");
            await Eventually(() => repo.Store.FindSymbols(scope, "Added", 10, repo.Token).Symbols.Count == 1, repo.Token, reports);
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
        var running = new RepositoryIndexWatcher(repo.Indexer, new GitSummaryIntegration(), repo.Exporter, new YamlRepositoryConfigurationLoader())
            .RunAsync(repo.Root, reports.Enqueue, cancellation.Token, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(100));
        try
        {
            await Eventually(() => reports.Any(report => report.Contains("failed", StringComparison.Ordinal)), repo.Token, reports);
            Assert.Equal(first.Snapshot.SnapshotId, repo.Store.GetSnapshot(first.Snapshot.Scope, repo.Token)!.SnapshotId);
            repo.Generator.Fail = false;
            await Eventually(() => repo.Store.FindSymbols(first.Snapshot.Scope, "Retry", 10, repo.Token).Symbols.Count == 1, repo.Token, reports);
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
    }

    [Fact]
    public async Task Watch_refreshes_shared_summaries_for_commit_after_each_reconciliation()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(repo.Token);
        var reports = new ConcurrentQueue<string>();
        var running = new RepositoryIndexWatcher(
            repo.Indexer, new GitSummaryIntegration(), repo.Exporter, new YamlRepositoryConfigurationLoader())
            .RunAsync(repo.Root, reports.Enqueue, cancellation.Token, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100));
        try
        {
            // The startup reconciliation exports descriptions even though no export command ran.
            await Eventually(() => File.Exists(repo.SummaryPath), repo.Token, reports);

            File.AppendAllText(Path.Combine(repo.Root, "Sample.cs"), "\nclass Refreshed {}\n");
            await Eventually(() => File.ReadAllText(repo.SummaryPath).Contains("Refreshed", StringComparison.Ordinal), repo.Token, reports);
            Assert.Contains(reports, report => report.StartsWith("Shared summaries refreshed:", StringComparison.Ordinal));
            Assert.DoesNotContain(reports, report => report.Contains("failed", StringComparison.Ordinal));
        }
        finally
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        }
    }

    private static async Task Eventually(
        Func<bool> condition, CancellationToken cancellationToken, IEnumerable<string>? reports = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline,
                "The watcher did not publish the expected state within 30 seconds. Reports so far: "
                + (reports is null ? "(not captured)" : string.Join(" | ", reports)));
            await Task.Delay(100, cancellationToken);
        }
    }
}
