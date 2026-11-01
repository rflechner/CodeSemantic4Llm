namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// Every MCP tool a repository server can advertise. The catalog is domain vocabulary: it is
/// the contract offered to coding agents, independent of the MCP SDK and of how the host wires
/// tool classes. The MCP server remains the only place that registers them, and a functional
/// test keeps registration and this catalog in step.
/// </summary>
public static class McpToolCatalog
{
    /// <summary>Catalog order groups read-only code tools first, then writing, then indexing.</summary>
    public static IReadOnlyList<McpTool> All { get; } = Array.AsReadOnly<McpTool>(
    [
        new("get_server_info", McpToolCapability.Code, "Server name and implementation status."),
        new("find_symbols", McpToolCapability.Code, "Finds declarations by exact simple name."),
        new("search_code", McpToolCapability.Code, "Ranks declarations from keywords or a question."),
        new("get_type_members", McpToolCapability.Code, "Lists the direct members of a type."),
        new("find_usages", McpToolCapability.Code, "Finds resolved references to a method or type."),
        new("get_method_body", McpToolCapability.Code, "Reads one method body."),
        new("get_method", McpToolCapability.Code, "Reads one complete method declaration."),
        new("validate_method_body", McpToolCapability.Code, "Checks a proposed body without writing."),
        new("get_method_diagnostics", McpToolCapability.Code, "Checks the syntax of one method on disk."),
        new("get_file_diagnostics", McpToolCapability.Code, "Checks the syntax of a whole file on disk."),
        new("get_type_diagnostics", McpToolCapability.Code, "Checks the syntax inside one type declaration."),
        new("replace_method_body", McpToolCapability.Writing, "Validates and replaces one method body."),
        new("replace_method", McpToolCapability.Writing, "Validates and replaces one complete method."),
        new("get_repository_configuration", McpToolCapability.Indexing, "Reads the repository's YAML settings."),
        new("preview_repository_index", McpToolCapability.Indexing, "Previews candidate source paths."),
        new("get_repository_index_status", McpToolCapability.Indexing, "Reports the saved snapshot and Git state.")
    ]);

    /// <summary>The catalog names in catalog order, for configuration files and error messages.</summary>
    public static IReadOnlyList<string> AllNames { get; } =
        Array.AsReadOnly(All.Select(tool => tool.Name).ToArray());

    /// <summary>Finds a tool by its exact, already normalized name.</summary>
    public static McpTool? Find(string? name) =>
        All.FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
}
