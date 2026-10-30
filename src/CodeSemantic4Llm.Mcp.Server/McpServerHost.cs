using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Mcp.Server.Tools;

namespace CodeSemantic4Llm.Mcp.Server;

/// <summary>Composes the stdio host with an optional operator-selected repository override.</summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync(string? repositoryRoot, bool allowWrite, CancellationToken cancellationToken)
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
        builder.Services.AddSingleton(new RepositoryWriteAccess(allowWrite));

        // Explicit registration keeps the editing tool undiscoverable in read-only mode.
        // A client cannot enable this capability through tool arguments or YAML settings.
        var mcp = builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<ServerInformationTools>()
            .WithTools<InspectSourceTools>()
            .WithTools<RepositoryConfigurationTools>()
            .WithTools<RepositoryIndexTools>()
            .WithTools<RepositoryMethodTools>();

        if (allowWrite)
        {
            mcp.WithTools<RepositoryWriteTools>();
        }

        using var host = builder.Build();
        await host.RunAsync(cancellationToken);
        return 0;
    }
}
