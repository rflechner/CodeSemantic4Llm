using System.Threading.Channels;
using CodeSemantic4Llm.Application.UseCases;

namespace CodeSemantic4Llm.Infrastructure.Workspace;

/// <summary>Serial, debounced reconciliation with startup verification and periodic recovery from missed events.</summary>
public sealed class RepositoryIndexWatcher(IndexRepository indexer, GitSummaryIntegration git)
{
    public async Task RunAsync(
        string root, Action<string>? report = null, CancellationToken cancellationToken = default,
        TimeSpan? reconciliationInterval = null)
    {
        root = Path.GetFullPath(root);
        var signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true
        });
        using var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        void Signal(object? sender, FileSystemEventArgs args)
        {
            var relative = Path.GetRelativePath(root, args.FullPath).Replace('\\', '/');
            if (!relative.Split('/').Any(part => part is "bin" or "obj") && !relative.EndsWith(".tmp", StringComparison.Ordinal))
            {
                signals.Writer.TryWrite(true);
            }
        }

        watcher.Changed += Signal;
        watcher.Created += Signal;
        watcher.Deleted += Signal;
        watcher.Renamed += (sender, args) => Signal(sender, args);
        watcher.Error += (_, _) => signals.Writer.TryWrite(true);
        watcher.EnableRaisingEvents = true;
        using var timer = new Timer(_ => signals.Writer.TryWrite(true), null,
            reconciliationInterval ?? TimeSpan.FromSeconds(30), reconciliationInterval ?? TimeSpan.FromSeconds(30));
        signals.Writer.TryWrite(true); // Start watching before the first scan so no startup change is lost.

        while (await signals.Reader.WaitToReadAsync(cancellationToken))
        {
            signals.Reader.TryRead(out _);
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            while (signals.Reader.TryRead(out _)) { }

            try
            {
                if (await git.IsOperationInProgressAsync(root, cancellationToken))
                {
                    report?.Invoke("Waiting for the Git operation/conflicts to finish before reconciling the index.");
                    continue;
                }

                var result = await indexer.ExecuteAsync(root, cancellationToken: cancellationToken);
                report?.Invoke($"Index verified: {result.AnalyzedFiles} analyzed, {result.ReusedFiles} reused.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // An HTTP timeout can also be an OperationCanceledException; only operator cancellation stops watching.
                // The index transaction has rolled back. The next event or timer tick retries the checkout.
                report?.Invoke("Index reconciliation failed; the previous snapshot remains available: " + exception.Message);
            }
        }
    }
}
