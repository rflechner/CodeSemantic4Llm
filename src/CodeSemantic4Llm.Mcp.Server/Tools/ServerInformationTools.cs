using System.ComponentModel;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Provides a truthful description of the currently implemented bootstrap.</summary>
[McpServerToolType]
public sealed class ServerInformationTools
{
    [McpServerTool, Description("Returns the server name and the current implementation status.")]
    public static string GetServerInfo() =>
        "CodeSemantic4Llm: MCP stdio host available. " +
        "The index command persists branch-scoped C# declarations and references. " +
        "Use find_symbols, get_type_members, find_usages and get_method_body to navigate source. " +
        "Method editing is available only with 'serve --allow-write'. " +
        "Generated descriptions and TypeScript/Angular parsing are not yet implemented.";
}
