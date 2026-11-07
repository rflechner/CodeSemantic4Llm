Feature: Distribute code summaries across independently authenticated LLM endpoints
  As a developer indexing a large repository with several LLM servers
  I want endpoint-local credentials and configurable fault tolerance
  So that I can use all available generation capacity safely

  Scenario: Endpoint authentication and concurrency settings survive a YAML roundtrip
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          model: same-model
          endpoints:
            - baseUrl: https://first.example.test/v1
              apiKey: token-one
            - baseUrl: https://second.example.test/v1
              apiKey: token-two
              apiKeyEnvironmentVariable: SECOND_FALLBACK
          maxConcurrentRequestsPerEndpoint: 2
          retryCount: 3
          retryDelayMilliseconds: 100
          maxConsecutiveFailedFiles: 6
      """
    When I write and reload the repository configuration
    Then the configured LLM endpoints are:
      | url                            | apiKey    | environment     |
      | https://first.example.test/v1  | token-one |                 |
      | https://second.example.test/v1 | token-two | SECOND_FALLBACK |
    And the LLM dispatch settings are 2, 3, 100 and 6

  Scenario: MCP configuration inspection does not leak endpoint API keys
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          model: same-model
          endpoints:
            - baseUrl: https://first.example.test/v1
              apiKey: token-one
            - baseUrl: https://second.example.test/v1
              apiKey: token-two
      """
    When MCP reads the repository LLM configuration
    Then the MCP configuration hides endpoint API keys

  Scenario: Mixing legacy and multiple endpoint formats is rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      semanticIndexing:
        llm:
          enabled: true
          model: same-model
          baseUrl: http://localhost:1234/v1
          endpoints:
            - baseUrl: https://first.example.test/v1
      """
    When I preview source indexing
    Then the preview fails with an error containing "either endpoints"
