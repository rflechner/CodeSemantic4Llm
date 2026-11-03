Feature: Customize the system prompt for generated code summaries
  As a repository owner
  I want to customize summary instructions in YAML
  So that generated descriptions follow my project's needs in the configured language

  Scenario: A multiline custom prompt survives writing and reloading configuration
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        language: FR-ch
        llm:
          systemPrompt: |-
            Summarize in {lang}.
            Include side effects and preserve {"example": "value"}.
      """
    When I write and reload the repository configuration
    Then the configured LLM system prompt is:
      """
      Summarize in {lang}.
      Include side effects and preserve {"example": "value"}.
      """
    And LLM calls remain disabled

  Scenario Outline: Absent or null custom prompts retain the built-in prompt
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: false
          <setting>
      """
    When I write and reload the repository configuration
    Then the default LLM system prompt remains selected

    Examples:
      | setting            |
      |                    |
      | systemPrompt: null |

  Scenario: Operators can inspect a custom system prompt through MCP
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          baseUrl: http://localhost:1234/v1
          model: local-test-model
          systemPrompt: Explain side effects in {lang}.
      """
    When MCP reads the repository LLM configuration
    Then the MCP configuration reports system prompt "Explain side effects in {lang}."

  Scenario Outline: Blank custom prompts fail configuration validation
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          systemPrompt: <value>
      """
    When I preview source indexing
    Then the preview fails with an error containing "systemPrompt cannot be blank"

    Examples:
      | value |
      | ""    |
      | "   " |
