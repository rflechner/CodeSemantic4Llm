using System.Diagnostics;
using System.Threading.Channels;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>
/// Serial, syntax-safe index reconciliation after a configurable filesystem quiet period. When shared
/// summaries are enabled, each successful reconciliation also refreshes the versioned Markdown so a
/// developer can commit descriptions together with the source changes that produced them.
/// </summary>
public sealed class RepositoryIndexWatcher(
    IndexRepository indexer, GitSummaryIntegration git, ExportSharedSummaries exporter,
    IRepositoryConfigurationLoader configurationLoader)
{
    /// <summary>
    /// Verifies the checkout at startup, debounces subsequent edits, and periodically recovers from missed
    /// events. A failed syntax check leaves the previous published snapshot in place.
    /// </summary>
    public async Task RunAsync(
        string root, Action<string>? report = null, CancellationToken cancellationToken = default,
        TimeSpan? reconciliationInterval = null, TimeSpan? debounceInterval = null)
    {
        root = Path.GetFullPath(root);
        var reconciliation = reconciliationInterval ?? TimeSpan.FromSeconds(30);
        var quietPeriod = debounceInterval ?? TimeSpan.FromSeconds(15);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reconciliation, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quietPeriod, TimeSpan.Zero);

        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        // Stopwatch timestamps are monotonic; a wall-clock correction must not shorten a quiet period.
        long lastFileEvent = 0;
        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        void SignalPath(string path)
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var excluded = relative.Split('/').Any(part =>
                part.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("obj", StringComparison.OrdinalIgnoreCase));

            if (excluded || relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Interlocked.Exchange(ref lastFileEvent, Stopwatch.GetTimestamp());
            signals.Writer.TryWrite(true);
        }

        watcher.Changed += (_, args) => SignalPath(args.FullPath);
        watcher.Created += (_, args) => SignalPath(args.FullPath);
        watcher.Deleted += (_, args) => SignalPath(args.FullPath);
        watcher.Renamed += (_, args) =>
        {
            // Moving a tracked source into an excluded directory still invalidates its old path.
            SignalPath(args.OldFullPath);
            SignalPath(args.FullPath);
        };
        watcher.Error += (_, _) => signals.Writer.TryWrite(true);
        watcher.EnableRaisingEvents = true;

        using var timer = new Timer(_ => signals.Writer.TryWrite(true), null, reconciliation, reconciliation);
        signals.Writer.TryWrite(true); // Subscribe before the first scan so startup changes are not lost.

        while (await signals.Reader.WaitToReadAsync(cancellationToken))
        {
            // Coalesce bursts, but do not let timer ticks reset the source-edit quiet period.
            bool newSignal;
            do
            {
                while (signals.Reader.TryRead(out _)) { }
                await WaitForQuietAsync(() => Interlocked.Read(ref lastFileEvent), quietPeriod, cancellationToken);
                newSignal = signals.Reader.TryRead(out _);
            } while (newSignal);

            try
            {
                if (await git.IsOperationInProgressAsync(root, cancellationToken))
                {
                    report?.Invoke("Waiting for the Git operation/conflicts to finish before reconciling the index.");
                    continue;
                }

                var result = await indexer.ExecuteAsync(
                    root, cancellationToken: cancellationToken, requireValidSyntax: true);
                report?.Invoke($"Index verified: {result.AnalyzedFiles} analyzed, {result.ReusedFiles} reused.");
                await RefreshSharedSummariesAsync(root, report, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Provider timeouts can also be OperationCanceledException; only operator cancellation stops watching.
                // A failed generation never replaces the published branch snapshot.
                report?.Invoke("Index reconciliation failed; the previous snapshot remains available: " + exception.Message);
            }
        }
    }

    /// <summary>
    /// Keeps the versioned Markdown in sync with the published index so descriptions can be committed
    /// with the code. A refresh failure never invalidates the published snapshot; the next cycle retries.
    /// </summary>
    private async Task RefreshSharedSummariesAsync(
        string root, Action<string>? report, CancellationToken cancellationToken)
    {
        try
        {
            var configuration = await configurationLoader.LoadAsync(root, cancellationToken);
            if (!configuration.SharedSummaries || !configuration.SemanticIndexing.Llm.Enabled)
            {
                return;
            }

            var exported = await exporter.ExecuteAsync(root, overwrite: true, cancellationToken);
            if (exported > 0)
            {
                report?.Invoke($"Shared summaries refreshed: {exported} file(s) ready to commit with the source.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            report?.Invoke("Shared summary refresh failed; the published index remains available: " + exception.Message);
        }
    }

    private static async Task WaitForQuietAsync(
        Func<long> lastFileEvent, TimeSpan quietPeriod, CancellationToken cancellationToken)
    {
        while (lastFileEvent() is var timestamp && timestamp != 0)
        {
            var remaining = quietPeriod - Stopwatch.GetElapsedTime(timestamp);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await Task.Delay(remaining, cancellationToken);
        }
    }
}
