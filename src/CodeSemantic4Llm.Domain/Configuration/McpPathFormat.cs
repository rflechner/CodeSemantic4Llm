namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>Source path fields included in MCP responses; tool inputs remain repository-relative.</summary>
public enum McpPathFormat
{
    /// <summary>Include both relativePath and absolutePath for compatibility.</summary>
    Both,

    /// <summary>Include only relativePath.</summary>
    Relative,

    /// <summary>Include only absolutePath, resolved against the selected checkout.</summary>
    Absolute
}
