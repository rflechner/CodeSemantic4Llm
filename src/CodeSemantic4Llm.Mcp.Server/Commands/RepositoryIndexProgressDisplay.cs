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

        _task!.IsIndeterminate = value.Phase is RepositoryIndexingPhase.Discovering or RepositoryIndexingPhase.Publishing;
        _task.Description = value.Phase switch
        {
            RepositoryIndexingPhase.Discovering => $"Discovering: {value.TotalFiles} files found",
            RepositoryIndexingPhase.Publishing => "Publishing snapshot",
            _ => $"{value.Phase}: {value.CompletedFiles}/{value.TotalFiles} files"
        };
        _task.MaxValue = Math.Max(1, value.TotalFiles);
        _task.Value = value.CompletedFiles;
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
