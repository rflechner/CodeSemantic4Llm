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
                .StartAsync(progress => indexRepository.RunAsync(
                    settings.RepositoryRoot, settings.Folder, new RepositoryIndexProgressDisplay(progress), cancellationToken, settings.Force))
            : await indexRepository.RunAsync(settings.RepositoryRoot, settings.Folder, cancellationToken: cancellationToken, force: settings.Force);

        console.WriteLine($"Repository: {result.State.Scope.RootPath}");
        console.WriteLine($"Index database: {storagePaths.GetDatabasePath(result.State.Scope.RootPath)}");
        console.WriteLine($"Branch: {result.State.Scope.BranchName}");
        console.WriteLine($"Recorded HEAD: {result.State.Revision ?? "unborn branch"}");
        if (result.PublishedSnapshot is { } snapshot)
        {
            console.WriteLine($"Indexed files: {snapshot.FileCount}; declarations: {snapshot.SymbolCount}");
            console.WriteLine($"Published snapshot: {snapshot.SnapshotId}");
        }
        else
        {
            console.WriteLine($"Indexed folder: {settings.Folder}; matching files: {result.AnalyzedFiles + result.ReusedFiles}");
            console.WriteLine("Run index without --folder to publish a complete branch snapshot.");
        }

        console.WriteLine($"Analyzed files: {result.AnalyzedFiles}; reused files: {result.ReusedFiles}");
        return 0;
    }
}
