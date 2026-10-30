using System.ComponentModel;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Default command: hosts MCP on stdio without writing human-readable output to stdout.</summary>
[Description("Start the stdio MCP server when no command is supplied.")]
public sealed class StartMcpServerCommand : AsyncCommand
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
        McpServerHost.RunAsync(null, allowWrite: false, cancellationToken);
}
