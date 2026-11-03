using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Options for continuously updating the checked-out branch's published index.</summary>
public sealed class WatchRepositorySettings : RepositoryCommandSettings
{
    /// <summary>Quiet period after the most recent source/configuration/summary change.</summary>
    [CommandOption("--debounce-seconds <SECONDS>")]
    [Description("Override watch.debounceSeconds from repository YAML (default: 15).")]
    public int? DebounceSeconds { get; init; }

    /// <summary>An explicit CLI value overrides the repository's validated YAML value.</summary>
    public TimeSpan ResolveDebounceInterval(RepositoryWatchConfiguration configuration) =>
        TimeSpan.FromSeconds(DebounceSeconds ?? configuration.DebounceSeconds);

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        var repositoryValidation = base.Validate();
        if (!repositoryValidation.Successful)
        {
            return repositoryValidation;
        }

        return DebounceSeconds is < RepositoryWatchConfiguration.MinDebounceSeconds or > RepositoryWatchConfiguration.MaxDebounceSeconds
            ? ValidationResult.Error("--debounce-seconds must be between 1 and 3600.")
            : ValidationResult.Success();
    }
}

/// <summary>Waits for source edits to settle before publishing only syntactically valid index generations.</summary>
public sealed class WatchRepositoryCommand(
    RepositoryIndexWatcher watcher, IRepositoryConfigurationLoader configurationLoader, IAnsiConsole console)
    : AsyncCommand<WatchRepositorySettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context, WatchRepositorySettings settings, CancellationToken cancellationToken)
    {
        var configuration = await configurationLoader.LoadAsync(settings.RepositoryRoot, cancellationToken);

        await watcher.RunAsync(settings.RepositoryRoot, message => console.WriteLine(message), cancellationToken,
            debounceInterval: settings.ResolveDebounceInterval(configuration.Watch));
        return 0;
    }
}
