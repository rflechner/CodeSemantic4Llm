using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Provides a truthful description of the currently implemented bootstrap.</summary>
[McpServerToolType]
public static class ServerInformationTools
{
    [McpServerTool, Description("Returns the server name and the current implementation status.")]
    public static string GetServerInfo() =>
        "CodeSemantic4Llm: MCP stdio host available. " +
        "The index command persists branch-scoped C# declaration snapshots for bounded symbol search and direct type-member navigation. " +
        "Semantic references, generated descriptions and TypeScript/Angular parsing are not yet implemented.";
}
