namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// The operator capability a tool belongs to. Capabilities are granted when the server
/// process starts; repository configuration can only narrow what a granted capability exposes.
/// </summary>
public enum McpToolCapability
{
    /// <summary>Read-only code search, navigation, reading and syntax diagnostics.</summary>
    Code,

    /// <summary>Source editing, granted by <c>serve --allow-write</c>.</summary>
    Writing,

    /// <summary>Configuration, preview and index status, granted by <c>serve --expose-indexing</c>.</summary>
    Indexing
}

/// <summary>
/// One MCP tool a repository server can advertise, identified by the exact name agents call.
/// The purpose is a short operator-facing line, not the model-facing tool description.
/// </summary>
public sealed record McpTool(string Name, McpToolCapability Capability, string Purpose);
