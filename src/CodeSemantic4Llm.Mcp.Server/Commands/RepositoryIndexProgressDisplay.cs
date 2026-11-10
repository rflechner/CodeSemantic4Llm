using CodeSemantic4Llm.Application.UseCases;
using Spectre.Console;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Applies progress inline, avoiding queued callbacks that could outlive the Spectre display.</summary>
internal sealed class RepositoryIndexProgressDisplay(ProgressContext context) : IProgress<RepositoryIndexingProgress>
{
    private ProgressTask? _task;
    private RepositoryIndexingPhase? _phase;

    public void Report(RepositoryIndexingProgress value)
    {
        if (_phase != value.Phase)
        {
            FinishTask();
            _phase = value.Phase;

            if (value.Phase is RepositoryIndexingPhase.Completed or RepositoryIndexingPhase.FolderCompleted)
            {
                return;
            }

            _task = context.AddTask(value.Phase.ToString(), maxValue: Math.Max(1, value.TotalFiles));
        }

        _task!.IsIndeterminate = value.Phase is RepositoryIndexingPhase.Discovering
            or RepositoryIndexingPhase.PreparingReferences
            or RepositoryIndexingPhase.PreparingCompilation
            or RepositoryIndexingPhase.SortingReferences
            or RepositoryIndexingPhase.CheckingCheckout
            or RepositoryIndexingPhase.Publishing;
        _task.Description = value.Phase switch
        {
            RepositoryIndexingPhase.Discovering => $"Discovering: {value.TotalFiles} files found",
            RepositoryIndexingPhase.PreparingReferences => "Preparing reference index (checking for reusable data)",
            RepositoryIndexingPhase.LoadingReferenceSources => $"Loading reference sources: {value.CompletedFiles}/{value.TotalFiles} files",
            RepositoryIndexingPhase.ParsingReferences => $"Parsing C# for references: {value.CompletedFiles}/{value.TotalFiles} files",
            RepositoryIndexingPhase.PreparingCompilation => "Building Roslyn reference compilation",
            RepositoryIndexingPhase.ResolvingReferences => $"Resolving references: {value.CompletedFiles}/{value.TotalFiles} files",
            RepositoryIndexingPhase.SortingReferences => "Ordering resolved references",
            RepositoryIndexingPhase.PersistingReferences => $"Saving references to SQLite: {value.CompletedFiles}/{value.TotalFiles} references",
            RepositoryIndexingPhase.CheckingCheckout => "Checking repository and configuration",
            RepositoryIndexingPhase.Publishing => "Publishing snapshot (SQLite)",
            _ => $"{value.Phase}: {value.CompletedFiles}/{value.TotalFiles} files"
        };
        _task.MaxValue = Math.Max(1, value.TotalFiles);
        _task.Value = _task.IsIndeterminate ? 0 : value.CompletedFiles;
    }

    private void FinishTask()
    {
        if (_task is not null)
        {
            _task.IsIndeterminate = false;
            _task.Value = _task.MaxValue;
            _task.StopTask();
        }
    }
}
