using System.ComponentModel;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Options for continuously updating the checked-out branch's published index.</summary>
public sealed class WatchRepositorySettings : RepositoryCommandSettings
{
    /// <summary>Quiet period after the most recent source/configuration/summary change.</summary>
    [CommandOption("--debounce-seconds <SECONDS>")]
    [Description("Wait this many seconds after the last filesystem event (default: 15).")]
    public int DebounceSeconds { get; init; } = 15;

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        var repositoryValidation = base.Validate();
        if (!repositoryValidation.Successful)
        {
            return repositoryValidation;
        }

        return DebounceSeconds is < 1 or > 3600
            ? ValidationResult.Error("--debounce-seconds must be between 1 and 3600.")
            : ValidationResult.Success();
    }
}

/// <summary>Waits for source edits to settle before publishing only syntactically valid index generations.</summary>
public sealed class WatchRepositoryCommand(RepositoryIndexWatcher watcher, IAnsiConsole console)
    : AsyncCommand<WatchRepositorySettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context, WatchRepositorySettings settings, CancellationToken cancellationToken)
    {
        await watcher.RunAsync(settings.RepositoryRoot, message => console.WriteLine(message), cancellationToken,
            debounceInterval: TimeSpan.FromSeconds(settings.DebounceSeconds));
        return 0;
    }
}
