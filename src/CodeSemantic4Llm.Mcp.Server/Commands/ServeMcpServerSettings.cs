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

    /// <summary>Explicitly enables MCP source writes for this server process only.</summary>
    [CommandOption("--allow-write")]
    [Description("Allow MCP clients to replace C# method bodies. Disabled by default.")]
    public bool AllowWrite { get; init; }

    /// <summary>Exposes operator-facing indexing tools and explicit snapshot metadata to MCP clients.</summary>
    [CommandOption("--expose-indexing")]
    [Description("Expose indexing tools, snapshot parameters and metadata to MCP clients. Hidden by default.")]
    public bool ExposeIndexing { get; init; }

    /// <summary>Writes diagnostics and complete tool parameters/replies to a rotated JSON-lines log.</summary>
    [CommandOption("--log-file <PATH>")]
    [Description("Write logs, including tool parameters and responses, to this file (relative to the working directory). Disabled by default.")]
    public string? LogFile { get; init; }

    /// <inheritdoc />
    public override ValidationResult Validate()
    {
        if (LogFile is not null && string.IsNullOrWhiteSpace(LogFile))
        {
            return ValidationResult.Error("The log file path cannot be empty.");
        }

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
