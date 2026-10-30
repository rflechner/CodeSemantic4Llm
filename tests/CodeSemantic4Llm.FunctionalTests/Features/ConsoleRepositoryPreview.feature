Feature: Preview a local repository from an IDE or terminal
  As a developer debugging repository discovery
  I want a console command that displays the files selected by the repository configuration
  So that I can inspect the result without connecting an MCP client

  Scenario: A console preview displays nested source files and effective settings
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      fileExtensions:
        dotnet-csharp: [.cs, .csharp]
      exclude: ["**/generated/**"]
      """
    And the repository contains these source files:
      | path                            |
      | src/Main.cs                     |
      | src/nested/Customer.csharp       |
      | src/generated/Generated.cs      |
      | src/bin/Generated.cs            |
      | src/app.ts                      |
    When I preview source indexing from the console
    Then the console preview succeeds
    And the console lists these files:
      | path                      | analyzer      |
      | src/Main.cs               | dotnet-csharp |
      | src/nested/Customer.csharp | dotnet-csharp |
    And the console output contains "Analyzer: dotnet-csharp (.cs, .csharp)"
    And the console output contains "Semantic description language: en"
    And the console output contains "Read-only discovery preview"
    And the console output contains "Files shown: 2"
    And the console output contains "All matching files are included."

  Scenario: A limited console preview explains that more matching files exist
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    And the repository contains these source files:
      | path                 |
      | src/First.cs         |
      | src/nested/Second.cs |
    When I preview source indexing from the console with a limit of "1"
    Then the console preview succeeds
    And the console lists these files:
      | path         | analyzer      |
      | src/First.cs | dotnet-csharp |
    And the console output contains "Files shown: 1"
    And the console output contains "More matching files are available"

  Scenario: An empty repository is a successful preview
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing from the console
    Then the console preview succeeds
    And the console output contains "Files shown: 0"
    And the console output contains "All matching files are included."

  Scenario: Missing YAML is reported as a console error
    Given the repository has no configuration file
    When I preview source indexing from the console
    Then the console preview fails with an error containing "Missing repository configuration"

  Scenario Outline: Invalid limits fail with a console error
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing from the console with a limit of "<limit>"
    Then the console preview fails with an error containing "<error>"

    Examples:
      | limit | error                               |
      | 0     | --max-files must be between 1 and 200 |
      | 201   | --max-files must be between 1 and 200 |
      | many  | Failed to convert 'many' to Int32    |

  Scenario: Help is available without repository configuration
    Given the repository has no configuration file
    When I request console preview help
    Then the console preview succeeds
    And the console output contains "codesemantic4llm"
    And the console output contains "preview"
    And the console output contains "<repository-path>"
    And the console output contains "--max-files"

  Scenario: A missing repository argument is rejected
    When I run the console preview without a repository path
    Then the console preview fails with an error containing "missing required argument 'repository-path'"
