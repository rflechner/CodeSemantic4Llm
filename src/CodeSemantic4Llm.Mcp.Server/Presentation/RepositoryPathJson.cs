using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Adds checkout-specific absolute paths to MCP JSON without persisting them in semantic data.</summary>
internal static class RepositoryPathJson
{
    /// <summary>
    /// Every response object with a relativePath receives an adjacent absolutePath contract,
    /// including nested locations, diagnostics and indexing candidates. Inputs remain relative.
    /// </summary>
    public static void AddAbsolutePath(JsonTypeInfo typeInfo, IConfiguration configuration)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object)
        {
            return;
        }

        var relativePath = typeInfo.Properties.FirstOrDefault(property =>
            property.Name == "relativePath" && property.PropertyType == typeof(string));
        if (relativePath?.Get is not { } getRelativePath)
        {
            return;
        }

        var absolutePath = typeInfo.CreateJsonPropertyInfo(typeof(string), "absolutePath");
        absolutePath.Get = instance =>
        {
            var root = configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
                ?? throw new InvalidOperationException("No repository root is configured for source locations.");
            var path = (string)getRelativePath(instance)!;
            return Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), Path.GetFullPath(root));
        };
        typeInfo.Properties.Add(absolutePath);
    }
}
