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
