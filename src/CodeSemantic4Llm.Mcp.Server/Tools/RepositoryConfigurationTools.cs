using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Configuration;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>
/// Repository access is explicitly limited to the checkout selected by the server operator.
/// Agents cannot choose an arbitrary filesystem path through MCP arguments.
/// </summary>
[McpServerToolType]
public sealed class RepositoryConfigurationTools(
    IConfiguration configuration,
    IRepositoryConfigurationLoader configurationLoader,
    PreviewRepositoryIndex previewRepositoryIndex)
{
    [McpServerTool, Description("Reads repository YAML settings, including the optional project name, extension overrides, default exclusions and the semantic indexing language.")]
    public Task<RepositoryIndexingConfiguration> GetRepositoryConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        return configurationLoader.LoadAsync(GetConfiguredRepositoryRoot(), cancellationToken);
    }

    [McpServerTool, Description("Recursively previews source paths with effective analyzer extensions, exclusions and semantic description language; does not index files.")]
    public Task<RepositoryIndexingPreview> PreviewRepositoryIndexAsync(
        [Description("Number of candidate files to return (1 to 200).")] int maxFiles = PreviewRepositoryIndex.DefaultMaxFiles,
        CancellationToken cancellationToken = default)
    {
        return previewRepositoryIndex.ExecuteAsync(
            GetConfiguredRepositoryRoot(), maxFiles, cancellationToken);
    }

    private string GetConfiguredRepositoryRoot()
    {
        var root = configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"];

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT " +
                "before calling repository-scoped MCP tools.");
        }

        return Path.GetFullPath(root);
    }
}
