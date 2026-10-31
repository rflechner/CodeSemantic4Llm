using CodeSemantic4Llm.Domain.Configuration;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class LlmSummaryConfigurationTests
{
    [Fact]
    public void Summaries_are_opt_in()
    {
        var settings = new SemanticIndexingConfiguration();
        Assert.False(settings.Llm.Enabled);
        Assert.Null(settings.Llm.BaseUrl);
        Assert.Null(settings.Llm.Model);
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
}
