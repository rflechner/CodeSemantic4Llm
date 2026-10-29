using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Default command: hosts MCP on stdio without writing human-readable output to stdout.</summary>
[Description("Start the stdio MCP server when no command is supplied.")]
public sealed class StartMcpServerCommand : AsyncCommand
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        // Stdio is reserved for MCP protocol messages. Human-readable logs go to stderr.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddRepositoryServices();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
