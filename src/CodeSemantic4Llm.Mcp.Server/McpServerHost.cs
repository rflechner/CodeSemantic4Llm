using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Mcp.Server.Logging;
using CodeSemantic4Llm.Mcp.Server.Presentation;
using CodeSemantic4Llm.Mcp.Server.Tools;

namespace CodeSemantic4Llm.Mcp.Server;

/// <summary>Composes the stdio host with an optional operator-selected repository override.</summary>
internal static class McpServerHost
{
    public static async Task<int> RunAsync(
        string? repositoryRoot, bool allowWrite, bool exposeIndexing, string? logFile, CancellationToken cancellationToken)
    {
        using var fileLogger = logFile is null ? null : new FileLoggerProvider(logFile);
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

        if (fileLogger is not null)
        {
            builder.Logging.AddProvider(fileLogger);
            builder.Logging.AddFilter<FileLoggerProvider>(null, LogLevel.Information);
            builder.Logging.AddFilter<FileLoggerProvider>("ModelContextProtocol", LogLevel.Warning);
        }

        builder.Services.AddRepositoryServices();
        builder.Services.AddSingleton(new RepositoryWriteAccess(allowWrite));
        builder.Services.AddSingleton(new McpToolExposure(exposeIndexing));
        builder.Services.AddSingleton<NavigationPages>();
        builder.Services.AddSingleton<CodeToolOperations>();
        builder.Services.AddSingleton<DiagnosticPages>();
        builder.Services.AddSingleton<CodeDiagnosticOperations>();

        // Explicit registration keeps the editing tool undiscoverable in read-only mode.
        // A client cannot enable this capability through tool arguments or YAML settings.
        var mcp = builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithRequestFilters(filters => filters.AddCallToolFilter(McpToolCallLogging.Wrap))
            .WithTools<ServerInformationTools>();
            // .WithTools<InspectSourceTools>()

        if (fileLogger is not null)
        {
            var payloadLogging = new McpToolPayloadLogging(fileLogger);
            mcp.WithMessageFilters(filters => filters
                .AddIncomingFilter(payloadLogging.WrapIncoming)
                .AddOutgoingFilter(payloadLogging.WrapOutgoing));
        }

        if (exposeIndexing)
        {
            mcp.WithTools<RepositoryConfigurationTools>()
                .WithTools<RepositoryDiagnosticsTools>()
                .WithTools<RepositoryIndexTools>()
                .WithTools<RepositoryMethodTools>();

            if (allowWrite)
            {
                mcp.WithTools<RepositoryWriteTools>();
            }
        }
        else
        {
            mcp.WithTools<RepositoryCodeTools>()
                .WithTools<RepositoryCodeDiagnosticsTools>();

            if (allowWrite)
            {
                mcp.WithTools<RepositoryCodeWriteTools>();
            }
        }

        using var host = builder.Build();

        if (fileLogger is not null)
        {
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("CodeSemantic4Llm.Mcp.Server")
                .LogInformation("File logging enabled: {LogFile}", fileLogger.FilePath);
        }

        await host.RunAsync(cancellationToken);
        return 0;
    }
}
