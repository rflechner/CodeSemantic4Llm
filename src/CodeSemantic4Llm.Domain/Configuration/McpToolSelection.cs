
namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// The MCP tools a repository allows its server to advertise. A selection only narrows what the
/// operator already granted when starting the process: it can hide a writing tool, but it can
/// never grant source editing or indexing exposure that the command line withheld.
/// </summary>
public sealed class McpToolSelection
{
    /// <summary>Every catalog tool, used when a repository's YAML declares no tools section.</summary>
    public static McpToolSelection All { get; } = new(McpToolCatalog.AllNames);

    /// <summary>
    /// Normalizes, deduplicates and validates requested tool names. Unknown names fail
    /// explicitly rather than being ignored, so a typo cannot silently hide a tool.
    /// </summary>
    public McpToolSelection(IEnumerable<string> toolNames)
    {
        ArgumentNullException.ThrowIfNull(toolNames);

        var requested = toolNames
            .Select(Normalize)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
        {
            throw new ArgumentException(
                "At least one MCP tool must be selected. Remove the tools section to advertise every tool.");
        }

        EnsureEveryToolExists(requested);

        // Catalog order keeps responses and generated configuration deterministic,
        // whatever order the repository's YAML happens to use.
        EnabledToolNames = Array.AsReadOnly(McpToolCatalog.AllNames
            .Where(name => requested.Contains(name, StringComparer.Ordinal))
            .ToArray());
    }

    /// <summary>Selected tool names in catalog order.</summary>
    public IReadOnlyList<string> EnabledToolNames { get; }

    /// <summary>Whether this repository allows the named tool to be advertised and called.</summary>
    public bool IsEnabled(string? toolName) =>
        toolName is not null && EnabledToolNames.Contains(toolName, StringComparer.Ordinal);

    private static void EnsureEveryToolExists(IEnumerable<string> requestedNames)
    {
        foreach (var name in requestedNames)
        {
            if (McpToolCatalog.Find(name) is null)
            {
                throw new ArgumentException(
                    $"Unknown MCP tool '{name}'. Available tools: {string.Join(", ", McpToolCatalog.AllNames)}.");
            }
        }
    }

    private static string Normalize(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("MCP tool names cannot be empty.");
        }

        return toolName.Trim().ToLowerInvariant();
    }
}
