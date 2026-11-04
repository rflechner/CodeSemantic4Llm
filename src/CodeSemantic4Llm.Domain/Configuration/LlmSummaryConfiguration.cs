using System.Text.Json.Serialization;
namespace CodeSemantic4Llm.Domain.Configuration;

/// <summary>Repository-local LLM settings with backwards-compatible single-endpoint authentication.</summary>
public sealed class LlmSummaryConfiguration
{
    public const int DefaultRetryCount = 3;
    public const int DefaultRetryDelayMilliseconds = 1000;
    public const int DefaultMaxConsecutiveFailedFiles = 6;
    public const int DefaultMaxConcurrentRequestsPerEndpoint = 1;

    public LlmSummaryConfiguration(
        bool enabled = false,
        string? baseUrl = null,
        string? model = null,
        string? apiKeyEnvironmentVariable = null,
        string? apiKey = null,
        string? reasoningEffort = null,
        int? maxCompletionTokens = null,
        bool forceGeneration = false,
        string? systemPrompt = null,
        IEnumerable<LlmEndpointConfiguration>? endpoints = null,
        int maxConcurrentRequestsPerEndpoint = DefaultMaxConcurrentRequestsPerEndpoint,
        int retryCount = DefaultRetryCount,
        int retryDelayMilliseconds = DefaultRetryDelayMilliseconds,
        int maxConsecutiveFailedFiles = DefaultMaxConsecutiveFailedFiles)
    {
        Enabled = enabled;
        ForceGeneration = forceGeneration;

        if (systemPrompt is not null && string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException("LLM systemPrompt cannot be blank.", nameof(systemPrompt));
        }

        SystemPrompt = systemPrompt;

        var configuredEndpoints = endpoints?.ToArray();

        if (configuredEndpoints is { Length: 0 } || configuredEndpoints?.Any(endpoint => endpoint is null) == true)
        {
            throw new ArgumentException("LLM endpoints must be a non-empty list of endpoint configurations.", nameof(endpoints));
        }

        if (configuredEndpoints is not null
            && (baseUrl is not null || apiKey is not null || apiKeyEnvironmentVariable is not null))
        {
            throw new ArgumentException(
                "Use either endpoints or the legacy baseUrl/authentication fields, not both.", nameof(endpoints));
        }

        var legacyEndpoint = baseUrl is null
            ? null
            : new LlmEndpointConfiguration(baseUrl, apiKeyEnvironmentVariable, apiKey);

        BaseUrl = legacyEndpoint?.BaseUrl;
        ApiKey = legacyEndpoint?.ApiKey ?? (string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
        ApiKeyEnvironmentVariable = legacyEndpoint?.ApiKeyEnvironmentVariable
            ?? LlmEndpointConfiguration.NormalizeEnvironmentVariable(apiKeyEnvironmentVariable);

        Endpoints = Array.AsReadOnly(configuredEndpoints ??
            (legacyEndpoint is null ? Array.Empty<LlmEndpointConfiguration>() : [legacyEndpoint]));

        if (model is not null && string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("LLM model cannot be blank.", nameof(model));
        }

        Model = model?.Trim();
        ReasoningEffort = NormalizeReasoningEffort(reasoningEffort);

        if (maxCompletionTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCompletionTokens), "LLM maxCompletionTokens must be positive.");
        }

        MaxCompletionTokens = maxCompletionTokens;

        if (maxConcurrentRequestsPerEndpoint is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentRequestsPerEndpoint),
                "LLM maxConcurrentRequestsPerEndpoint must be between 1 and 32.");
        }

        if (retryCount is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(retryCount), "LLM retryCount must be between 0 and 10.");
        }

        if (retryDelayMilliseconds is < 0 or > 60000)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelayMilliseconds),
                "LLM retryDelayMilliseconds must be between 0 and 60000.");
        }

        if (maxConsecutiveFailedFiles is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConsecutiveFailedFiles),
                "LLM maxConsecutiveFailedFiles must be between 1 and 1000.");
        }

        MaxConcurrentRequestsPerEndpoint = maxConcurrentRequestsPerEndpoint;
        RetryCount = retryCount;
        RetryDelayMilliseconds = retryDelayMilliseconds;
        MaxConsecutiveFailedFiles = maxConsecutiveFailedFiles;

        if (enabled && (Endpoints.Count == 0 || Model is null))
        {
            throw new ArgumentException("Enabled LLM indexing requires model and at least one baseUrl or endpoints entry.");
        }
    }

    public bool Enabled { get; }
    public bool ForceGeneration { get; }
    public string? SystemPrompt { get; }

    /// <summary>Legacy single endpoint URL; null when using the endpoints list.</summary>
    public Uri? BaseUrl { get; }

    /// <summary>Independent endpoints, including a legacy baseUrl represented as a single entry.</summary>
    public IReadOnlyList<LlmEndpointConfiguration> Endpoints { get; }

    public string? Model { get; }
    public string? ApiKeyEnvironmentVariable { get; }
    [JsonIgnore]
    public string? ApiKey { get; }
    public string? ReasoningEffort { get; }
    public int? MaxCompletionTokens { get; }

    /// <summary>Maximum simultaneous HTTP requests per endpoint.</summary>
    public int MaxConcurrentRequestsPerEndpoint { get; }

    /// <summary>Number of additional attempts after the first failed request, per endpoint.</summary>
    public int RetryCount { get; }

    public int RetryDelayMilliseconds { get; }

    /// <summary>Stop a run after this many consecutive failed files, not failed HTTP attempts.</summary>
    public int MaxConsecutiveFailedFiles { get; }

    [JsonIgnore]
    public Uri ChatCompletionsEndpoint => BaseUrl is null
        ? throw new InvalidOperationException("LLM legacy baseUrl is not configured.")
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
