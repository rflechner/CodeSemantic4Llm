using System.ComponentModel;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>The target checkout is required; indexing configuration comes from its root YAML.</summary>
public sealed class RepositoryIndexSettings : RepositoryCommandSettings
{
    /// <summary>Bypasses SQLite file and reference reuse for this run.</summary>
    [CommandOption("--force")]
    [Description("Analyze every selected file, even when a matching indexed file exists.")]
    public bool Force { get; init; }
}
