using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Hosts MCP with an optional repository argument; the default command remains argument-free.</summary>
public sealed class ServeMcpServerCommand : AsyncCommand<ServeMcpServerSettings>
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(
        CommandContext context, ServeMcpServerSettings settings, CancellationToken cancellationToken) =>
        McpServerHost.RunAsync(settings.RepositoryRoot, settings.AllowWrite, cancellationToken);
}
