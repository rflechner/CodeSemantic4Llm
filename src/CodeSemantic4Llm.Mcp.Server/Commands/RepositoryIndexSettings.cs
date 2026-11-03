using System.ComponentModel;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>The target checkout is required; indexing configuration comes from its root YAML.</summary>
public sealed class RepositoryIndexSettings : RepositoryCommandSettings
{
    /// <summary>Limits the run to one folder inside the checkout and leaves the result partial.</summary>
    [CommandOption("--folder <PATH>")]
    [Description("Index only this repository-relative folder, then stop before full-branch publication.")]
    public string? Folder { get; init; }

    /// <summary>Bypasses SQLite file and reference reuse for this run.</summary>
    [CommandOption("--force")]
    [Description("Analyze every selected file, even when a matching indexed file exists.")]
    public bool Force { get; init; }
}
