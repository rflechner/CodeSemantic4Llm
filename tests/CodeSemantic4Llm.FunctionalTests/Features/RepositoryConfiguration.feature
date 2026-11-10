Feature: Per-repository YAML indexing configuration
  As a developer indexing several repositories
  I want the checkout to declare its analyzers and excluded paths
  So that the semantic index only considers the source I have selected

  Scenario: An absent watch section uses the default quiet period
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing
    Then the configured watch debounce is 15 seconds

  Scenario: A repository selects its own watch quiet period
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      watch:
        debounceSeconds: 6
      """
    When I preview source indexing
    Then the configured watch debounce is 6 seconds

  Scenario Outline: Invalid watch quiet periods are rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      watch:
        debounceSeconds: <value>
      """
    When I preview source indexing
    Then the preview fails with an error containing "<message>"

    Examples:
      | value | message               |
      | 0     | watch.debounceSeconds |
      | -1    | watch.debounceSeconds |
      | 3601  | watch.debounceSeconds |
      | nope  | Invalid YAML          |

  Scenario: A project display name is normalized and exposed by preview
    Given the repository configuration is:
      """
      version: 1
      projectName: "  Équipe: Paiements #1 [API]  "
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing
    Then the project name is "Équipe: Paiements #1 [API]"
    When I preview source indexing from the console
    Then the console preview succeeds
    And the console output contains "Project: Équipe: Paiements #1 [API]"

  Scenario Outline: Invalid project display names fail explicitly
    Given the repository configuration is:
      """
      version: 1
      projectName: <name>
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing
    Then the preview fails with an error containing "<error>"

    Examples:
      | name               | error                    |
      | ""                 | name cannot be empty     |
      | "   "              | name cannot be empty     |
      | [First, Second]    | Invalid YAML             |

  Scenario: A multiline project name is rejected
    Given the repository configuration is:
      """
      version: 1
      projectName: |
        First line
        Second line
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing
    Then the preview fails with an error containing "control characters"

  Scenario: Duplicate project name keys are rejected
    Given the repository configuration is:
      """
      version: 1
      projectName: First
      projectName: Second
      analyzers: [dotnet-csharp]
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid YAML"

  Scenario: Default build folders and an explicitly excluded submodule are never indexed
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      exclude:
        - submodules/**
      """
    And the repository contains these source files:
      | path                                  |
      | src/Customer.cs                       |
      | src/Shared.cs                         |
      | src/app.ts                            |
      | submodules/vendor/External.cs         |
      | bin/Debug/net10.0/Generated.cs        |
      | src/obj/Debug/net10.0/Generated.cs    |
      | .git/hooks/Generated.cs               |
    When I preview source indexing
    Then the indexable files are:
      | path            | analyzer      |
      | src/Customer.cs | dotnet-csharp |
      | src/Shared.cs   | dotnet-csharp |
    And the effective exclusions include "**/bin/**"
    And the effective exclusions include "**/obj/**"
    And the effective exclusions include "**/.git/**"

  Scenario: A Git submodule is included unless excluded by YAML
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      """
    And the repository contains these source files:
      | path                         |
      | src/Main.cs                  |
      | submodules/vendor/External.cs|
    When I preview source indexing
    Then the indexable files are:
      | path                          | analyzer      |
      | src/Main.cs                   | dotnet-csharp |
      | submodules/vendor/External.cs | dotnet-csharp |

  Scenario: Directory names without globs also exclude their contents
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      exclude:
        - third_party
        - "**/*.g.cs"
      """
    And the repository contains these source files:
      | path                       |
      | src/Main.cs                |
      | src/Generated.g.cs         |
      | src/SomeOther.cs           |
      | third_party/vendor/Lib.cs  |
    When I preview source indexing
    Then the indexable files are:
      | path             | analyzer      |
      | src/Main.cs      | dotnet-csharp |
      | src/SomeOther.cs | dotnet-csharp |

  Scenario: An unknown or not-yet-implemented analyzer fails explicitly
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
        - typescript
      """
    And the repository contains these source files:
      | path        |
      | src/Main.cs |
    When I preview source indexing
    Then the preview fails with an error containing "typescript"

  Scenario: Missing YAML is rejected instead of inheriting settings from elsewhere
    Given the repository has no configuration file
    When I preview source indexing
    Then the preview fails with an error containing "Missing repository configuration"

  Scenario: Unexpected YAML keys are rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      unknownSetting: true
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid YAML"

  Scenario: Unsupported configuration versions are rejected
    Given the repository configuration is:
      """
      version: 2
      analyzers:
        - dotnet-csharp
      """
    When I preview source indexing
    Then the preview fails with an error containing "Unsupported configuration version"

  Scenario: Preview results are bounded and report that more candidates exist
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      """
    And the repository contains these source files:
      | path          |
      | src/First.cs  |
      | src/Second.cs |
    When I preview source indexing with a limit of 1 file
    Then the preview contains 1 file
    And the preview indicates that more files are available

  Scenario: Repository paths cannot escape the checkout through exclusions
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      exclude:
        - ../other-repository/**
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid indexing configuration"

  Scenario Outline: Invalid MCP path formats fail explicitly
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      mcpPaths: <value>
      """
    When I preview source indexing
    Then the preview fails with an error containing "<error>"

    Examples:
      | value   | error        |
      | unknown | mcpPaths     |
      | 1       | mcpPaths     |
      | null    | mcpPaths     |
      | ""      | mcpPaths     |
      | []      | Invalid YAML |

  Scenario Outline: Writing repository configuration preserves the MCP path format
    Given the repository configuration is:
      """
      version: 1
      analyzers: [dotnet-csharp]
      mcpPaths: <format>
      """
    When I write and reload the repository configuration
    Then the configured MCP path format is "<format>"

    Examples:
      | format   |
      | relative |
      | absolute |
      | both     |
