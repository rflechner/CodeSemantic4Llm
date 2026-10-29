using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Declarative options and bounds for a read-only repository preview.</summary>
public sealed class RepositoryPreviewSettings : RepositoryCommandSettings
{
    /// <summary>Maximum number of matching paths to display.</summary>
    [CommandOption("--max-files <count>")]
    [Description("Maximum number of files to display (1-200).")]
    [DefaultValue(PreviewRepositoryIndex.DefaultMaxFiles)]
    public int MaxFiles { get; init; } = PreviewRepositoryIndex.DefaultMaxFiles;

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        var repositoryValidation = base.Validate();

        if (!repositoryValidation.Successful)
        {
            return repositoryValidation;
        }

        return MaxFiles is < 1 or > PreviewRepositoryIndex.MaximumMaxFiles
            ? ValidationResult.Error($"--max-files must be between 1 and {PreviewRepositoryIndex.MaximumMaxFiles}.")
            : ValidationResult.Success();
    }
}
