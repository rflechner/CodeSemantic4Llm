using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server;

/// <summary>Composes the stdio host with an optional operator-selected repository override.</summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync(string? repositoryRoot, CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();

        if (repositoryRoot is not null)
        {
            // Add last so CLI selection overrides environment settings without changing process state.
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CODESEMANTIC4LLM_REPOSITORY_ROOT"] = Path.GetFullPath(repositoryRoot)
            });
        }

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
