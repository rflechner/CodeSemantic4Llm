Feature: Configure reasoning for generated code summaries
  As a developer testing different LLM endpoints
  I want optional reasoning effort and completion budgets in each repository
  So that I can reduce reasoning when supported without changing other endpoints

  Scenario: Writing configuration preserves reasoning settings
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          baseUrl: http://localhost:1234/v1
          model: local-test-model
          reasoningEffort: LOW
          maxCompletionTokens: 4096
      """
    When I write and reload the repository configuration
    Then the configured LLM reasoning effort is "low"
    And the configured LLM completion budget is 4096

  Scenario: Omitted reasoning settings remain absent after writing configuration
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    When I write and reload the repository configuration
    Then no LLM reasoning settings are configured or written

  Scenario: Operators can inspect the configured reasoning settings through MCP
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          baseUrl: http://localhost:1234/v1
          model: local-test-model
          reasoningEffort: low
          maxCompletionTokens: 2048
      """
    When MCP reads the repository LLM configuration
    Then the MCP configuration reports reasoning effort "low" and completion budget 2048

  Scenario Outline: Invalid generation settings fail configuration validation
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          <setting>: <value>
      """
    When I preview source indexing
    Then the preview fails with an error containing "<setting>"

    Examples:
      | setting             | value  |
      | reasoningEffort     | fast   |
      | reasoningEffort     | ""     |
      | maxCompletionTokens | 0      |
      | maxCompletionTokens | -1     |
