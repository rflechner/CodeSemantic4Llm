using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Persistence.Sqlite;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Builds a complete persisted index before MCP startup, with interactive Spectre progress.</summary>
public sealed class RepositoryIndexCommand(IndexRepository indexRepository, IndexStoragePaths storagePaths, IAnsiConsole console)
    : AsyncCommand<RepositoryIndexSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context, RepositoryIndexSettings settings, CancellationToken cancellationToken)
    {
        var result = console.Profile.Capabilities.Interactive
            ? await console.Progress().AutoClear(true)
                .Columns(new SpinnerColumn(), new TaskDescriptionColumn(), new ProgressBarColumn(),
                    new PercentageColumn(), new ElapsedTimeColumn())
                .StartAsync(progress => indexRepository.ExecuteAsync(
                    settings.RepositoryRoot, new RepositoryIndexProgressDisplay(progress), cancellationToken, settings.Force))
            : await indexRepository.ExecuteAsync(settings.RepositoryRoot, cancellationToken: cancellationToken, force: settings.Force);

        console.WriteLine($"Repository: {result.Snapshot.Scope.RootPath}");
        console.WriteLine($"Index database: {storagePaths.GetDatabasePath(result.Snapshot.Scope.RootPath)}");
        console.WriteLine($"Branch: {result.Snapshot.Scope.BranchName}");
        console.WriteLine($"Recorded HEAD: {result.Snapshot.RecordedRevision ?? "unborn branch"}");
        console.WriteLine($"Indexed files: {result.Snapshot.FileCount}; declarations: {result.Snapshot.SymbolCount}");
        console.WriteLine($"Analyzed files: {result.AnalyzedFiles}; reused files: {result.ReusedFiles}");
        console.WriteLine($"Published snapshot: {result.Snapshot.SnapshotId}");
        return 0;
    }
}
