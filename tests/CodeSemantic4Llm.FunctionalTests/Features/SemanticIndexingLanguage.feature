Feature: Language for natural-language semantic indexing
  As a repository maintainer
  I want to configure the language of generated semantic descriptions
  So that agents can search code using descriptions in my chosen human language

  Scenario: English is the default when no semantic indexing language is configured
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      """
    When I preview source indexing
    Then the semantic indexing language is "en"

  Scenario: A repository can request French semantic descriptions
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing:
        language: fr
      """
    And the repository contains these source files:
      | path          |
      | src/Client.cs |
    When I preview source indexing
    Then the semantic indexing language is "fr"
    And the indexable files are:
      | path          | analyzer      |
      | src/Client.cs | dotnet-csharp |

  Scenario: The language code can include a region
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing:
        language: FR-ch
      """
    When I preview source indexing
    Then the semantic indexing language is "fr-CH"

  Scenario: An empty semantic indexing section keeps the English default
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing: {}
      """
    When I preview source indexing
    Then the semantic indexing language is "en"

  Scenario: An invalid language code is rejected explicitly
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing:
        language: french_CH
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid indexing configuration"

  Scenario: An explicitly empty language is invalid
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing:
        language: ""
      """
    When I preview source indexing
    Then the preview fails with an error containing "semantic indexing language cannot be empty"

  Scenario: Unknown semantic indexing settings are rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      semanticIndexing:
        language: en
        translateCode: true
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid YAML"
