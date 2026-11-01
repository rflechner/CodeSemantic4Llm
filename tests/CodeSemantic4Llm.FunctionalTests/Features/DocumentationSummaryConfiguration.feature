Feature: Configure whether LLM summaries replace source documentation
  As a repository owner
  I want an explicit YAML override for documented declarations
  So that I can choose generated summaries when testing a model

  Scenario Outline: Writing configuration preserves the documentation override
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: false
          forceGeneration: <force>
      """
    When I write and reload the repository configuration
    Then the configured LLM documentation override is "<force>"
    And the documentation override has not enabled LLM calls

    Examples:
      | force |
      | true  |
      | false |

  Scenario: Omitted documentation override prefers existing summaries
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    When I write and reload the repository configuration
    Then the configured LLM documentation override is "false"
    And the documentation override has not enabled LLM calls

  Scenario: Operators can inspect the documentation override through MCP
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          baseUrl: http://localhost:1234/v1
          model: local-test-model
          forceGeneration: true
      """
    When MCP reads the repository LLM configuration
    Then the MCP configuration reports the documentation override as "true"

  Scenario: The override must be a boolean
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          forceGeneration: always
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid YAML"
