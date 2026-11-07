using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>An independently authenticated OpenAI-compatible endpoint.</summary>
public sealed class LlmEndpointConfiguration
{
    private static readonly Regex EnvironmentVariablePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public LlmEndpointConfiguration(
        string baseUrl, string? apiKeyEnvironmentVariable = null, string? apiKey = null)
    {
        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            throw new ArgumentException(
                "LLM endpoint baseUrl must be an absolute HTTP(S) URL without credentials or query parameters.",
                nameof(baseUrl));
        }

        BaseUrl = uri;
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        ApiKeyEnvironmentVariable = NormalizeEnvironmentVariable(apiKeyEnvironmentVariable);
    }

    public Uri BaseUrl { get; }

    /// <summary>Optional inline Bearer token. Never include this in logs or MCP responses.</summary>
    [JsonIgnore]
    public string? ApiKey { get; }

    /// <summary>Fallback variable for a Bearer token; each endpoint resolves its own variable.</summary>
    public string? ApiKeyEnvironmentVariable { get; }

    [JsonIgnore]
    public Uri ChatCompletionsEndpoint =>
        new(BaseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions", UriKind.Absolute);

    internal static string? NormalizeEnvironmentVariable(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();

        if (!EnvironmentVariablePattern.IsMatch(normalized))
        {
            throw new ArgumentException(
                "LLM apiKeyEnvironmentVariable must be a valid environment variable name.", nameof(value));
        }

        return normalized;
    }
}
