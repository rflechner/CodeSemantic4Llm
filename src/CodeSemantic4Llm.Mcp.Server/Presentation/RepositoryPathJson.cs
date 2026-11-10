using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Configuration;
using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Adds checkout-specific absolute paths to MCP JSON without persisting them in semantic data.</summary>
internal static class RepositoryPathJson
{
    // Serialization metadata is cached by the SDK. Read the request-local choice in
    // predicates instead of capturing the first request's settings in that metadata.
    private static readonly AsyncLocal<McpPathFormat> RequestFormat = new();

    public static McpPathFormat CurrentFormat
    {
        get => RequestFormat.Value;
        set => RequestFormat.Value = value;
    }

    /// <summary>
    /// Every response object with a relativePath can expose an adjacent absolutePath contract,
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

        relativePath.ShouldSerialize = (_, _) => CurrentFormat != McpPathFormat.Absolute;

        var absolutePath = typeInfo.CreateJsonPropertyInfo(typeof(string), "absolutePath");
        absolutePath.Get = instance =>
        {
            var root = configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
                ?? throw new InvalidOperationException("No repository root is configured for source locations.");
            var path = (string)getRelativePath(instance)!;
            return Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar), Path.GetFullPath(root));
        };
        absolutePath.ShouldSerialize = (_, _) => CurrentFormat != McpPathFormat.Relative;
        typeInfo.Properties.Add(absolutePath);
    }
}
