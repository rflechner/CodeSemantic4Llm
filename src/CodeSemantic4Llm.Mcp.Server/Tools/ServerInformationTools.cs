using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Provides a truthful description of the currently implemented bootstrap.</summary>
[McpServerToolType]
public sealed class ServerInformationTools(McpToolExposure exposure, RepositoryWriteAccess writeAccess)
{
    [McpServerTool, Description("Returns the server name and the current implementation status.")]
    public string GetServerInfo()
    {
        var capabilities = "CodeSemantic4Llm: C# symbol search, navigation and syntax diagnostics. " +
            (writeAccess.Enabled ? "Method body editing is enabled." : "Source access is read-only.");

        return exposure.ExposeIndexing
            ? capabilities + " Indexing tools and snapshot metadata are exposed. " +
              "Run the index command after source changes. Queries use saved branch-scoped declarations and references."
            : capabilities;
    }
}

/// <summary>Operator-selected tool presentation; requests and repository YAML cannot enable it.</summary>
public sealed record McpToolExposure(bool ExposeIndexing);
