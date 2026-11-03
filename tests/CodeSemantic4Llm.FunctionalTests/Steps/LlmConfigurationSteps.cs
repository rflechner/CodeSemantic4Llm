using System.Text.Json;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Configuration;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

public sealed partial class RepositoryConfigurationSteps
{
    private RepositoryIndexingConfiguration? _loadedConfiguration;
    private JsonElement _llmConfigurationResponse;

    [Then("the configured LLM system prompt is:")]
    public void ThenSystemPrompt(string expected) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), _loadedConfiguration!.SemanticIndexing.Llm.SystemPrompt);

    [Then("the default LLM system prompt remains selected")]
    public void ThenDefaultSystemPrompt() => Assert.Null(_loadedConfiguration!.SemanticIndexing.Llm.SystemPrompt);

    [Then("the MCP configuration reports system prompt \"(.*)\"")]
    public void ThenMcpSystemPrompt(string expected)
    {
        var llm = McpCodeClient.ReadContent(_llmConfigurationResponse).GetProperty("semanticIndexing").GetProperty("llm");
        Assert.Equal(expected, llm.GetProperty("systemPrompt").GetString());
    }

    [Then("the configured LLM documentation override is \"(.*)\"")]
    public void ThenDocumentationOverride(string expected) =>
        Assert.Equal(bool.Parse(expected), _loadedConfiguration!.SemanticIndexing.Llm.ForceGeneration);

    [Then("the documentation override has not enabled LLM calls")]
    [Then("LLM calls remain disabled")]
    public void ThenLlmRemainsDisabled() => Assert.False(_loadedConfiguration!.SemanticIndexing.Llm.Enabled);

    [Then("the MCP configuration reports the documentation override as \"(.*)\"")]
    public void ThenMcpDocumentationOverride(string expected)
    {
        var llm = McpCodeClient.ReadContent(_llmConfigurationResponse).GetProperty("semanticIndexing").GetProperty("llm");
        Assert.Equal(bool.Parse(expected), llm.GetProperty("forceGeneration").GetBoolean());
    }

    [When("I write and reload the repository configuration")]
    public async Task WhenRoundTripLlmConfiguration()
    {
        var token = TestContext.Current.CancellationToken;
        var loader = new YamlRepositoryConfigurationLoader();
        var configuration = await loader.LoadAsync(_repositoryRoot, token);
        var destination = Path.Combine(_repositoryRoot, "roundtrip");
        Directory.CreateDirectory(destination);

        await new YamlRepositoryConfigurationWriter().CreateAsync(destination, configuration, token);
        _loadedConfiguration = await loader.LoadAsync(destination, token);
    }

    [Then(@"the configured LLM API key is ""(.*)""")]
    public void ThenInlineLlmApiKey(string expected) =>
        Assert.Equal(expected, _loadedConfiguration!.SemanticIndexing.Llm.ApiKey);

    [Then(@"the configured LLM API key environment variable is ""(.*)""")]
    public void ThenLlmApiKeyEnvironmentVariable(string expected) =>
        Assert.Equal(expected, _loadedConfiguration!.SemanticIndexing.Llm.ApiKeyEnvironmentVariable);

    [Then(@"the configured LLM reasoning effort is ""(.*)""")]
    public void ThenLlmReasoningEffort(string expected) =>
        Assert.Equal(expected, _loadedConfiguration!.SemanticIndexing.Llm.ReasoningEffort);

    [Then(@"the configured LLM completion budget is (\d+)")]
    public void ThenLlmCompletionBudget(int expected) =>
        Assert.Equal(expected, _loadedConfiguration!.SemanticIndexing.Llm.MaxCompletionTokens);

    [Then("no LLM reasoning settings are configured or written")]
    public void ThenNoLlmReasoningSettings()
    {
        Assert.Null(_loadedConfiguration!.SemanticIndexing.Llm.ReasoningEffort);
        Assert.Null(_loadedConfiguration.SemanticIndexing.Llm.MaxCompletionTokens);
        var yaml = File.ReadAllText(Path.Combine(_repositoryRoot, "roundtrip", "codesemantic4llm.yaml"));
        Assert.DoesNotContain("reasoningEffort", yaml);
        Assert.DoesNotContain("maxCompletionTokens", yaml);
    }

    [Then(@"the MCP configuration reports reasoning effort ""(.*)"" and completion budget (\d+)")]
    public void ThenMcpReportsReasoningSettings(string expectedEffort, int expectedBudget)
    {
        var llm = McpCodeClient.ReadContent(_llmConfigurationResponse).GetProperty("semanticIndexing").GetProperty("llm");
        Assert.Equal(expectedEffort, llm.GetProperty("reasoningEffort").GetString());
        Assert.Equal(expectedBudget, llm.GetProperty("maxCompletionTokens").GetInt32());
    }

    [When("MCP reads the repository LLM configuration")]
    public async Task WhenReadLlmConfigurationThroughMcp()
    {
        _loadedConfiguration = await new YamlRepositoryConfigurationLoader().LoadAsync(
            _repositoryRoot, TestContext.Current.CancellationToken);
        await using var client = await McpCodeClient.StartAsync(
            _repositoryRoot, Path.Combine(_repositoryRoot, "cache"),
            allowWrite: false, exposeIndexing: true, TestContext.Current.CancellationToken);
        _llmConfigurationResponse = await client.CallAsync("get_repository_configuration", new { });
    }

    [Then("the MCP configuration omits the inline API key")]
    public void ThenMcpOmitsInlineKey()
    {
        var content = McpCodeClient.ReadContent(_llmConfigurationResponse);
        Assert.False(content.GetProperty("semanticIndexing").GetProperty("llm").TryGetProperty("apiKey", out _));
        Assert.DoesNotContain(_loadedConfiguration!.SemanticIndexing.Llm.ApiKey!, _llmConfigurationResponse.GetRawText());
    }

    [Then("the MCP configuration retains the LLM model and environment variable name")]
    public void ThenMcpRetainsLlmSettings()
    {
        var llm = McpCodeClient.ReadContent(_llmConfigurationResponse).GetProperty("semanticIndexing").GetProperty("llm");
        Assert.Equal(_loadedConfiguration!.SemanticIndexing.Llm.Model, llm.GetProperty("model").GetString());
        Assert.Equal(_loadedConfiguration.SemanticIndexing.Llm.ApiKeyEnvironmentVariable,
            llm.GetProperty("apiKeyEnvironmentVariable").GetString());
    }
}
