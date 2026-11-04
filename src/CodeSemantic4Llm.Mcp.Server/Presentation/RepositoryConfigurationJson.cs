using System.Text.Json.Serialization.Metadata;
using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Keeps inline credentials out of MCP configuration responses and their payload logs.</summary>
internal static class RepositoryConfigurationJson
{
    public static void RemoveSecrets(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(LlmSummaryConfiguration) &&
            typeInfo.Type != typeof(LlmEndpointConfiguration))
        {
            return;
        }

        var apiKeyName = typeInfo.Options.PropertyNamingPolicy?.ConvertName(nameof(LlmSummaryConfiguration.ApiKey))
            ?? nameof(LlmSummaryConfiguration.ApiKey);
        var apiKey = typeInfo.Properties.FirstOrDefault(property => property.Name == apiKeyName);

        if (apiKey is not null)
        {
            typeInfo.Properties.Remove(apiKey);
        }
    }
}
