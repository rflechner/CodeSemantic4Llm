using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>
/// Applies a repository's tool selection to the tools this server instance advertises and accepts.
/// The selection can only hide tools: source editing and indexing exposure remain granted
/// exclusively by the process capabilities, never by repository configuration.
/// </summary>
internal sealed class McpToolAvailability(
    IConfiguration configuration,
    IRepositoryConfigurationLoader configurationLoader,
    ILogger<McpToolAvailability> logger)
{
    /// <summary>Hides deselected tools from discovery, leaving registered definitions untouched.</summary>
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> WrapList(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) => async (context, cancellationToken) =>
    {
        var result = await next(context, cancellationToken);
        var availability = context.Services!.GetRequiredService<McpToolAvailability>();
        var selection = await availability.GetSelectionAsync(cancellationToken);

        if (selection is null)
        {
            return result;
        }

        // Copy the response rather than the registered definitions, which the SDK reuses.
        return new ListToolsResult
        {
            Tools = result.Tools.Where(tool => selection.IsEnabled(tool.Name)).ToList(),
            NextCursor = result.NextCursor,
            Meta = result.Meta,
            TimeToLive = result.TimeToLive,
            CacheScope = result.CacheScope
        };
    };

    /// <summary>Refuses a deselected tool, so a stale client tool list cannot reach it.</summary>
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> WrapCall(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (context, cancellationToken) =>
    {
        var availability = context.Services!.GetRequiredService<McpToolAvailability>();
        var selection = await availability.GetSelectionAsync(cancellationToken);
        var requested = context.Params?.Name;

        if (requested is not null && selection is not null && !selection.IsEnabled(requested))
        {
            throw new McpException(
                $"Tool '{requested}' is not enabled for this project. " +
                "Ask the server operator to select it in the repository configuration, then refresh the tool list.");
        }

        return await next(context, cancellationToken);
    };

    /// <summary>
    /// Reads the current selection, or null when no repository is selected or its configuration
    /// cannot be read. Discovery and diagnostics must keep working without valid indexing
    /// settings, so an unreadable file leaves the process capabilities as the only restriction.
    /// Re-reading per request lets an operator's edit take effect after a tool-list refresh.
    /// </summary>
    private async Task<McpToolSelection?> GetSelectionAsync(CancellationToken cancellationToken)
    {
        var root = configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"];

        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        try
        {
            var settings = await configurationLoader.LoadAsync(root, cancellationToken);
            return settings.Tools;
        }
        catch (Exception exception) when (exception is RepositoryConfigurationException or UnauthorizedAccessException)
        {
            // Do not log parser errors here: they may contain confidential YAML values.
            logger.LogWarning("Cannot read the MCP tool selection from repository configuration; advertising every tool this server is allowed to expose.");
            return null;
        }
    }
}
