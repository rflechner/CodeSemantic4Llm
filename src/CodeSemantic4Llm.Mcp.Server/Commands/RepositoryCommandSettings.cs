using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Common positional argument for commands operating on an operator-selected repository.</summary>
public abstract class RepositoryCommandSettings : CommandSettings
{
    /// <summary>Absolute path or path relative to the process working directory.</summary>
    [CommandArgument(0, "<repository-path>")]
    [Description("Path to the target repository (absolute or relative).")]
    public string RepositoryRoot { get; init; } = string.Empty;

    /// <inheritdoc />
    public override ValidationResult Validate() => string.IsNullOrWhiteSpace(RepositoryRoot)
        ? ValidationResult.Error("Expected a repository path.")
        : ValidationResult.Success();
}
