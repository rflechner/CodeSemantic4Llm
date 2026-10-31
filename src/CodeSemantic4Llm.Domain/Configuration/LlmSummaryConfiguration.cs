using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// Opt-in, repository-local LLM settings. Only the name of an environment variable may
/// appear here; API keys themselves must not be stored in repository YAML.
/// </summary>
public sealed class LlmSummaryConfiguration
{
    private static readonly Regex EnvironmentVariablePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public LlmSummaryConfiguration(
        bool enabled = false,
        string? baseUrl = null,
        string? model = null,
        string? apiKeyEnvironmentVariable = null)
    {
        Enabled = enabled;

        if (baseUrl is not null)
        {
            if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            {
                throw new ArgumentException("LLM baseUrl must be an absolute HTTP(S) URL without credentials or query parameters.", nameof(baseUrl));
            }

            BaseUrl = uri;
        }

        if (model is not null && string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("LLM model cannot be blank.", nameof(model));
        }

        Model = model?.Trim();

        if (apiKeyEnvironmentVariable is not null)
        {
            var variable = apiKeyEnvironmentVariable.Trim();

            if (!EnvironmentVariablePattern.IsMatch(variable))
            {
                throw new ArgumentException("LLM apiKeyEnvironmentVariable must be a valid environment variable name.", nameof(apiKeyEnvironmentVariable));
            }

            ApiKeyEnvironmentVariable = variable;
        }

        if (enabled && (BaseUrl is null || Model is null))
        {
            throw new ArgumentException("Enabled LLM indexing requires both baseUrl and model.");
        }
    }

    public bool Enabled { get; }

    /// <summary>OpenAI-compatible API root, for example http://127.0.0.1:1234/v1.</summary>
    public Uri? BaseUrl { get; }

    /// <summary>Model name used in chat completion requests.</summary>
    public string? Model { get; }

    /// <summary>Optional variable containing a Bearer token; never an inline secret.</summary>
    public string? ApiKeyEnvironmentVariable { get; }

    public Uri ChatCompletionsEndpoint => BaseUrl is null
        ? throw new InvalidOperationException("LLM baseUrl is not configured.")
        : new Uri(BaseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions", UriKind.Absolute);
}
