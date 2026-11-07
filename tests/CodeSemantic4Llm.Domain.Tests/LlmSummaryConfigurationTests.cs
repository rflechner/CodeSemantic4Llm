using CodeSemantic4Llm.Domain.Configuration;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class LlmSummaryConfigurationTests
{
    [Fact]
    public void Llm_summaries_are_opt_in()
    {
        var settings = new SemanticIndexingConfiguration();
        Assert.False(settings.Llm.Enabled);
        Assert.False(settings.Llm.ForceGeneration);
        Assert.Null(settings.Llm.BaseUrl);
        Assert.Null(settings.Llm.Model);
        Assert.Null(settings.Llm.SystemPrompt);
        Assert.Null(settings.Llm.ApiKey);
        Assert.Null(settings.Llm.ReasoningEffort);
        Assert.Null(settings.Llm.MaxCompletionTokens);
    }

    [Fact]
    public void Enabled_settings_resolve_openai_compatible_completions_url()
    {
        var settings = new LlmSummaryConfiguration(true, "http://localhost:1234/v1/", "local-model", "MODEL_API_KEY");
        Assert.Equal("http://localhost:1234/v1/chat/completions", settings.ChatCompletionsEndpoint.ToString());
        Assert.Equal("MODEL_API_KEY", settings.ApiKeyEnvironmentVariable);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void Blank_system_prompts_are_rejected_even_when_generation_is_disabled(string prompt)
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(systemPrompt: prompt));
    }

    [Fact]
    public void Custom_system_prompt_preserves_whitespace_and_does_not_enable_generation()
    {
        const string prompt = "  Summarize in {lang}.\n    Keep indentation.\n";
        var settings = new LlmSummaryConfiguration(systemPrompt: prompt);

        Assert.Equal(prompt, settings.SystemPrompt);
        Assert.False(settings.Enabled);
    }

    [Theory]
    [InlineData(null, "model")]
    [InlineData("http://localhost:1234/v1", null)]
    public void Enabled_settings_require_url_and_model(string? url, string? model)
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(true, url, model));
    }

    [Theory]
    [InlineData("ftp://localhost/index")]
    [InlineData("http://user:secret@localhost/v1")]
    [InlineData("https://example.com/v1?secret=key")]
    public void Url_does_not_accept_unsupported_schemes_or_inline_secrets(string url)
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(true, url, "model"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  local-token  ", "local-token")]
    public void Inline_api_keys_are_normalized(string? value, string? expected)
    {
        var settings = new LlmSummaryConfiguration(apiKey: value);

        Assert.Equal(expected, settings.ApiKey);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("none", "none")]
    [InlineData("minimal", "minimal")]
    [InlineData(" LOW ", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "xhigh")]
    [InlineData("max", "max")]
    public void Reasoning_effort_is_optional_and_normalized(string? value, string? expected)
    {
        Assert.Equal(expected, new LlmSummaryConfiguration(reasoningEffort: value).ReasoningEffort);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("fast")]
    [InlineData("disabled")]
    public void Unknown_or_blank_reasoning_effort_is_rejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(reasoningEffort: value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Completion_token_budget_must_be_positive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmSummaryConfiguration(maxCompletionTokens: value));
    }
    [Fact]
    public void Multiple_endpoints_have_independent_credentials_and_dispatch_settings()
    {
        var first = new LlmEndpointConfiguration("http://localhost:1234/v1", "FIRST_KEY");
        var second = new LlmEndpointConfiguration("https://second.example.test/v1", apiKey: "second-secret");
        var settings = new LlmSummaryConfiguration(
            enabled: true, model: "shared-model", endpoints: [first, second],
            maxConcurrentRequestsPerEndpoint: 2, retryCount: 4,
            retryDelayMilliseconds: 250, maxConsecutiveFailedFiles: 6);

        Assert.Null(settings.BaseUrl);
        Assert.Equal(2, settings.Endpoints.Count);
        Assert.Equal("FIRST_KEY", settings.Endpoints[0].ApiKeyEnvironmentVariable);
        Assert.Equal("second-secret", settings.Endpoints[1].ApiKey);
        Assert.Equal("https://second.example.test/v1/chat/completions",
            settings.Endpoints[1].ChatCompletionsEndpoint.ToString());
        Assert.Equal(2, settings.MaxConcurrentRequestsPerEndpoint);
        Assert.Equal(4, settings.RetryCount);
        Assert.Equal(250, settings.RetryDelayMilliseconds);
        Assert.Equal(6, settings.MaxConsecutiveFailedFiles);
    }

    [Fact]
    public void Single_endpoint_settings_keep_the_legacy_configuration_compatible()
    {
        var settings = new LlmSummaryConfiguration(true, "http://localhost:1234/v1",
            "model", "LLM_KEY", "inline-secret");

        var endpoint = Assert.Single(settings.Endpoints);
        Assert.Equal(settings.BaseUrl, endpoint.BaseUrl);
        Assert.Equal(settings.ApiKey, endpoint.ApiKey);
        Assert.Equal(settings.ApiKeyEnvironmentVariable, endpoint.ApiKeyEnvironmentVariable);
        Assert.Equal(1, settings.MaxConcurrentRequestsPerEndpoint);
        Assert.Equal(3, settings.RetryCount);
        Assert.Equal(6, settings.MaxConsecutiveFailedFiles);
    }

    [Fact]
    public void Legacy_authentication_and_endpoint_list_cannot_be_combined()
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(
            enabled: true, baseUrl: "http://localhost:1234/v1", model: "model",
            endpoints: [new LlmEndpointConfiguration("http://localhost:5678/v1")]));
    }

    [Fact]
    public void Empty_endpoint_lists_are_not_accepted()
    {
        Assert.Throws<ArgumentException>(() => new LlmSummaryConfiguration(
            enabled: true, model: "model", endpoints: []));
    }

    [Theory]
    [InlineData(0, 3, 100, 6)]
    [InlineData(33, 3, 100, 6)]
    [InlineData(1, -1, 100, 6)]
    [InlineData(1, 11, 100, 6)]
    [InlineData(1, 3, -1, 6)]
    [InlineData(1, 3, 100, 0)]
    public void Dispatch_settings_are_bounded(int concurrency, int retry, int delay, int failureThreshold)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlmSummaryConfiguration(
            maxConcurrentRequestsPerEndpoint: concurrency, retryCount: retry,
            retryDelayMilliseconds: delay, maxConsecutiveFailedFiles: failureThreshold));
    }

}
