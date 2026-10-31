using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Identifies the selected project in every tool advertised by this server instance.</summary>
internal sealed class ProjectToolDescriptions(
    IConfiguration configuration,
    IRepositoryConfigurationLoader configurationLoader,
    ILogger<ProjectToolDescriptions> logger)
{
    public static McpRequestHandler<ListToolsRequestParams, ListToolsResult> Wrap(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next) => async (context, cancellationToken) =>
    {
        var result = await next(context, cancellationToken);
        var descriptions = context.Services!.GetRequiredService<ProjectToolDescriptions>();
        var projectName = await descriptions.GetProjectNameAsync(cancellationToken);

        if (projectName is null)
        {
            return result;
        }

        // SDK tool definitions can be reused across requests. Copy the response instead of
        // modifying registered descriptions, so repeated discovery never accumulates prefixes.
        return new ListToolsResult
        {
            Tools = result.Tools.Select(tool => DescribeProject(tool, projectName)).ToList(),
            NextCursor = result.NextCursor,
            Meta = result.Meta,
            TimeToLive = result.TimeToLive,
            CacheScope = result.CacheScope
        };
    };

    private async Task<string?> GetProjectNameAsync(CancellationToken cancellationToken)
    {
        var root = configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"];

        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var directoryName = new DirectoryInfo(Path.GetFullPath(root)).Name;

        try
        {
            var settings = await configurationLoader.LoadAsync(root, cancellationToken);
            return settings.ProjectName ?? directoryName;
        }
        catch (Exception exception) when (exception is RepositoryConfigurationException or UnauthorizedAccessException)
        {
            // Discovery must remain available for source diagnostics even without valid indexing
            // settings. Do not log parser errors here: they may contain confidential YAML values.
            logger.LogWarning("Cannot read the project name from repository configuration; using the directory name for MCP tool descriptions.");
            return directoryName;
        }
    }

    private static Tool DescribeProject(Tool tool, string projectName) => new()
    {
        Name = tool.Name,
        Title = tool.Title,
        Description = $"In project {projectName}, {tool.Description}",
        InputSchema = tool.InputSchema,
        OutputSchema = tool.OutputSchema,
        Annotations = tool.Annotations,
        Icons = tool.Icons,
        Meta = tool.Meta
    };
}
