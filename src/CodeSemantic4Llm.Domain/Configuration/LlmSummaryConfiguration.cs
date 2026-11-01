using System.Text.RegularExpressions;

namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>
/// Opt-in, repository-local LLM settings. An inline API key takes precedence over
/// the optional environment variable used for authentication.
/// </summary>
public sealed class LlmSummaryConfiguration
{
    private static readonly Regex EnvironmentVariablePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public LlmSummaryConfiguration(
        bool enabled = false,
        string? baseUrl = null,
        string? model = null,
        string? apiKeyEnvironmentVariable = null,
        string? apiKey = null,
        string? reasoningEffort = null,
        int? maxCompletionTokens = null,
        bool forceGeneration = false)
    {
        Enabled = enabled;
        ForceGeneration = forceGeneration;

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
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        ReasoningEffort = NormalizeReasoningEffort(reasoningEffort);

        if (maxCompletionTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCompletionTokens), "LLM maxCompletionTokens must be positive.");
        }

        MaxCompletionTokens = maxCompletionTokens;

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

    /// <summary>
    /// When LLM indexing is enabled, generates summaries even for declarations with source documentation.
    /// Does not enable LLM calls by itself or bypass reuse of unchanged indexed files.
    /// </summary>
    public bool ForceGeneration { get; }

    /// <summary>OpenAI-compatible API root, for example http://127.0.0.1:1234/v1.</summary>
    public Uri? BaseUrl { get; }

    /// <summary>Model name used in chat completion requests.</summary>
    public string? Model { get; }

    /// <summary>Optional variable containing a Bearer token, used only when ApiKey is absent.</summary>
    public string? ApiKeyEnvironmentVariable { get; }

    /// <summary>Optional inline Bearer token. Blank values are treated as absent; surrounding whitespace is removed.</summary>
    public string? ApiKey { get; }

    /// <summary>
    /// Optional reasoning effort sent to compatible chat completion APIs. Null keeps the server default;
    /// supported levels vary by provider and model and are not inferred from the model name.
    /// </summary>
    public string? ReasoningEffort { get; }

    /// <summary>Optional completion token budget, including reasoning tokens on APIs that count them.</summary>
    public int? MaxCompletionTokens { get; }

    public Uri ChatCompletionsEndpoint => BaseUrl is null
        ? throw new InvalidOperationException("LLM baseUrl is not configured.")
        : new Uri(BaseUrl.AbsoluteUri.TrimEnd('/') + "/chat/completions", UriKind.Absolute);

    private static string? NormalizeReasoningEffort(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "none" or "minimal" or "low" or "medium" or "high" or "xhigh" or "max"
            ? normalized
            : throw new ArgumentException(
                "LLM reasoningEffort must be one of: none, minimal, low, medium, high, xhigh, max.", nameof(value));
    }
}
