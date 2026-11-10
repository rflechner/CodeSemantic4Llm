using CodeSemantic4Llm.Application.UseCases;
using Spectre.Console;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Reports phase transitions and bounded milestones when stdout is not interactive.</summary>
internal sealed class RepositoryIndexTextProgressDisplay(IAnsiConsole console) : IProgress<RepositoryIndexingProgress>
{
    private RepositoryIndexingPhase? _phase;
    private int _lastDecile = -1;

    public void Report(RepositoryIndexingProgress value)
    {
        if (value.Phase is RepositoryIndexingPhase.Completed or RepositoryIndexingPhase.FolderCompleted)
        {
            console.WriteLine("Index published successfully.");
            _phase = value.Phase;
            return;
        }

        var description = Description(value.Phase);

        if (_phase != value.Phase)
        {
            _phase = value.Phase;
            _lastDecile = -1;
            console.WriteLine($"{description}...");
        }

        // Unknown-duration phases emit a clear activity label rather than a misleading percentage.
        if (value.TotalFiles == 0 || value.Phase is RepositoryIndexingPhase.Discovering
            or RepositoryIndexingPhase.PreparingReferences
            or RepositoryIndexingPhase.PreparingCompilation
            or RepositoryIndexingPhase.SortingReferences
            or RepositoryIndexingPhase.CheckingCheckout
            or RepositoryIndexingPhase.Publishing)
        {
            return;
        }

        var decile = (int)((long)value.CompletedFiles * 10 / value.TotalFiles);

        if (decile > _lastDecile)
        {
            _lastDecile = decile;
            var percent = (long)value.CompletedFiles * 100 / value.TotalFiles;
            console.WriteLine($"{description}: {value.CompletedFiles}/{value.TotalFiles} ({percent}%)");
        }
    }

    private static string Description(RepositoryIndexingPhase phase) => phase switch
    {
        RepositoryIndexingPhase.Discovering => "Discovering source files",
        RepositoryIndexingPhase.Indexing => "Indexing declarations",
        RepositoryIndexingPhase.PreparingReferences => "Checking for reusable reference data",
        RepositoryIndexingPhase.LoadingReferenceSources => "Loading reference sources",
        RepositoryIndexingPhase.ParsingReferences => "Parsing C# for references",
        RepositoryIndexingPhase.PreparingCompilation => "Building Roslyn reference compilation",
        RepositoryIndexingPhase.ResolvingReferences => "Resolving references",
        RepositoryIndexingPhase.SortingReferences => "Ordering resolved references",
        RepositoryIndexingPhase.PersistingReferences => "Saving references to SQLite",
        RepositoryIndexingPhase.Verifying => "Verifying source files",
        RepositoryIndexingPhase.CheckingCheckout => "Checking repository and configuration",
        RepositoryIndexingPhase.Publishing => "Publishing snapshot (SQLite)",
        _ => phase.ToString()
    };
}
