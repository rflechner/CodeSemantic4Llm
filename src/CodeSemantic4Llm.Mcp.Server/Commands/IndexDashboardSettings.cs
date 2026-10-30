using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Local, read-only HTTP dashboard options.</summary>
public sealed class IndexDashboardSettings : CommandSettings
{
    /// <summary>HTTP port bound exclusively to the loopback address.</summary>
    [CommandOption("--port <PORT>")]
    [Description("Loopback HTTP port for the dashboard (default: 5080).")]
    public int Port { get; init; } = 5080;

    /// <inheritdoc />
    public override ValidationResult Validate() => Port is >= 1 and <= 65535
        ? ValidationResult.Success()
        : ValidationResult.Error("The dashboard port must be between 1 and 65535.");
}
