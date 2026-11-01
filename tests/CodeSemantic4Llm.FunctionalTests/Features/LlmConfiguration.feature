Feature: Configure repository-local LLM authentication
  As a developer using an authenticated LLM endpoint
  I want to keep an inline API key with an optional environment fallback
  So that I can configure indexing without requiring an environment variable

  Background:
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          baseUrl: http://localhost:1234/v1
          model: local-test-model
          apiKey: "local:token#sample"
          apiKeyEnvironmentVariable: FALLBACK_LLM_API_KEY
      """

  Scenario: Writing repository configuration preserves both authentication options
    When I write and reload the repository configuration
    Then the configured LLM API key is "local:token#sample"
    And the configured LLM API key environment variable is "FALLBACK_LLM_API_KEY"

  Scenario: MCP configuration inspection omits inline credentials
    When MCP reads the repository LLM configuration
    Then the MCP configuration omits the inline API key
    And the MCP configuration retains the LLM model and environment variable name

