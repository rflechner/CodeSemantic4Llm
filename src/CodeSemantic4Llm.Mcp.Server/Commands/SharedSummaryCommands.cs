using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Only explicit export changes versioned summaries.</summary>
public sealed class ExportSummariesSettings : RepositoryCommandSettings
{
    [CommandOption("--overwrite")]
    [Description("Replace differing Markdown after reviewing local edits. Never stages or commits files.")]
    public bool Overwrite { get; init; }
}

/// <summary>Exports descriptions from a verified local snapshot for manual Git review.</summary>
public sealed class ExportSummariesCommand(ExportSharedSummaries exporter, IAnsiConsole console)
    : AsyncCommand<ExportSummariesSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ExportSummariesSettings settings, CancellationToken cancellationToken)
    {
        var count = await exporter.ExecuteAsync(settings.RepositoryRoot, settings.Overwrite, cancellationToken);
        console.WriteLine($"Exported {count} summary files. Review .codesemantic4llm/summaries before staging them with Git.");
        return 0;
    }
}

/// <summary>Repository selection shared by watcher and Git setup commands.</summary>
public sealed class SharedSummaryRepositorySettings : RepositoryCommandSettings;

/// <summary>Installs the scoped attribute and local executable driver definition.</summary>
public sealed class SetupSummaryGitCommand(GitSummaryIntegration git, IAnsiConsole console)
    : AsyncCommand<SharedSummaryRepositorySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SharedSummaryRepositorySettings settings, CancellationToken cancellationToken)
    {
        await git.SetupAsync(settings.RepositoryRoot, cancellationToken);
        console.WriteLine("Installed the local summary merge driver and .gitattributes rule. The codesemantic4llm tool must be on PATH.");
        return 0;
    }
}

/// <summary>Git provides temporary paths. Policy names describe Git sides, never an inferred remote.</summary>
public sealed class MergeSummarySettings : CommandSettings
{
    [CommandArgument(0, "<policy>")] public string Policy { get; init; } = "";
    [CommandArgument(1, "<ancestor>")] public string Ancestor { get; init; } = "";
    [CommandArgument(2, "<current>")] public string Current { get; init; } = "";
    [CommandArgument(3, "<other>")] public string Other { get; init; } = "";

    public override ValidationResult Validate() => Policy is "current" or "other"
        ? ValidationResult.Success() : ValidationResult.Error("Merge policy must be 'current' or 'other'.");
}

/// <summary>Selects one complete valid Git input without inspecting partially merged source files.</summary>
public sealed class MergeSummaryCommand(ISharedSummaryCodec codec) : AsyncCommand<MergeSummarySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, MergeSummarySettings settings, CancellationToken cancellationToken)
    {
        var selected = settings.Policy == "other" ? settings.Other : settings.Current;
        if (new FileInfo(selected).Length > 4 * 1024 * 1024)
        {
            throw new IOException("Merge input exceeds the shared summary size limit.");
        }

        var text = await File.ReadAllTextAsync(selected, cancellationToken);
        codec.Decode(text); // Reject conflict markers and malformed documents instead of reporting false success.
        if (settings.Policy == "other")
        {
            await File.WriteAllTextAsync(settings.Current, text, cancellationToken);
        }

        return 0;
    }
}
