using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Optional repository selection for MCP; omission preserves environment-based configuration.</summary>
public sealed class ServeMcpServerSettings : CommandSettings
{
    /// <summary>Overrides the configured repository, resolving relative paths from the process working directory.</summary>
    [CommandArgument(0, "[repository-path]")]
    [Description("Repository directory (absolute or relative). Overrides CODESEMANTIC4LLM_REPOSITORY_ROOT.")]
    public string? RepositoryRoot { get; init; }

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        if (RepositoryRoot is null)
        {
            return ValidationResult.Success();
        }

        if (string.IsNullOrWhiteSpace(RepositoryRoot))
        {
            return ValidationResult.Error("The repository path cannot be empty.");
        }

        var root = Path.GetFullPath(RepositoryRoot);
        return Directory.Exists(root)
            ? ValidationResult.Success()
            : ValidationResult.Error($"Repository directory '{root}' does not exist.");
    }
}
