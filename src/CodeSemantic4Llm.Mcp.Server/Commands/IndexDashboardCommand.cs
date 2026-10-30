using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Mcp.Server.Dashboard;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Starts a browser-based, read-only explorer of all locally cached indexes.</summary>
public sealed class IndexDashboardCommand(IIndexDashboardReader reader, IAnsiConsole console)
    : AsyncCommand<IndexDashboardSettings>
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(
        CommandContext context, IndexDashboardSettings settings, CancellationToken cancellationToken) =>
        DashboardHost.RunAsync(reader, settings.Port, console, cancellationToken);
}
