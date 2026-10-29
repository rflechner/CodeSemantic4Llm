Feature: Recursive source discovery by configured file extensions
  As a developer working with a repository containing nested projects
  I want recursive discovery with sensible and overridable extensions per analyzer
  So that I can select source files without changing the language adapters

  Scenario: Default extensions find source files at the root and in deeply nested folders
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      """
    And the repository contains these source files:
      | path                                      |
      | Root.cs                                   |
      | src/Program.cs                            |
      | src/features/customers/models/Customer.CS |
      | src/features/orders/Order.cs              |
      | src/features/customers/models/Customer.ts |
      | src/Program.cs.bak                        |
      | src/README                                |
    When I preview source indexing
    Then the indexable files are returned in this order:
      | path                                      | analyzer      |
      | Root.cs                                   | dotnet-csharp |
      | src/Program.cs                            | dotnet-csharp |
      | src/features/customers/models/Customer.CS | dotnet-csharp |
      | src/features/orders/Order.cs              | dotnet-csharp |
    And the effective extensions for "dotnet-csharp" are:
      | extension |
      | .cs       |

  Scenario: An override replaces an analyzer's default extensions
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: [.csharp]
      """
    And the repository contains these source files:
      | path                                |
      | Program.cs                          |
      | src/features/models/Customer.cs     |
      | src/features/models/Customer.CSHARP |
      | src/features/models/Customer.ts     |
    When I preview source indexing
    Then the indexable files are:
      | path                                | analyzer      |
      | src/features/models/Customer.CSHARP | dotnet-csharp |
    And the effective extensions for "dotnet-csharp" are:
      | extension |
      | .csharp   |

  Scenario: An override can retain defaults and add a custom extension
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        DOTNET-CSHARP: [" .CS ", .csharp, .cs]
      """
    And the repository contains these source files:
      | path                       |
      | src/Program.cs             |
      | src/models/Customer.csharp |
    When I preview source indexing
    Then the indexable files are:
      | path                       | analyzer      |
      | src/Program.cs             | dotnet-csharp |
      | src/models/Customer.csharp | dotnet-csharp |
    And the effective extensions for "dotnet-csharp" are:
      | extension |
      | .cs       |
      | .csharp   |

  Scenario: Custom extensions still respect all exclusions at any depth
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: [.csharp]
      exclude:
        - "**/generated/**"
        - "**/*.g.csharp"
      """
    And the repository contains these source files:
      | path                                 |
      | src/models/Customer.csharp           |
      | src/models/Customer.g.csharp         |
      | src/models/generated/Customer.csharp |
      | src/models/bin/Customer.csharp       |
      | src/models/obj/Customer.csharp       |
      | src/models/.git/Customer.csharp      |
      | submodules/vendor/External.csharp    |
    When I preview source indexing
    Then the indexable files are:
      | path                              | analyzer      |
      | src/models/Customer.csharp        | dotnet-csharp |
      | submodules/vendor/External.csharp | dotnet-csharp |

  Scenario: Recursive traversal skips linked directories and linked files
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      """
    And the repository contains these source files:
      | path        |
      | src/Main.cs |
    And a directory link "src/outside" points outside the repository
    And a directory link "src/loop" points to the repository root
    And a file link "src/Linked.cs" points outside the repository
    When I preview source indexing
    Then the indexable files are:
      | path        | analyzer      |
      | src/Main.cs | dotnet-csharp |

  Scenario: The preview limit applies to matching files across nested directories
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: [.csharp]
      """
    And the repository contains these source files:
      | path                 |
      | Ignored.cs           |
      | a/deep/First.csharp  |
      | b/deep/Second.csharp |
    When I preview source indexing with a limit of 1 file
    Then the indexable files are:
      | path                | analyzer      |
      | a/deep/First.csharp | dotnet-csharp |
    And the preview indicates that more files are available

  Scenario: Nonmatching files do not make a complete preview appear truncated
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: [.csharp]
      """
    And the repository contains these source files:
      | path                |
      | First.csharp        |
      | src/deep/Ignored.cs |
    When I preview source indexing with a limit of 1 file
    Then the indexable files are:
      | path         | analyzer      |
      | First.csharp | dotnet-csharp |
    And the preview indicates that all matching files are included

  Scenario Outline: Invalid extension selections fail explicitly
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: <extensions>
      """
    When I preview source indexing
    Then the preview fails with an error containing "<error>"

    Examples:
      | extensions | error                       |
      | []         | At least one file extension |
      | null       | must be a non-empty list    |
      | [null]     | Invalid file extension      |
      | [""]       | Invalid file extension      |
      | [cs]       | Invalid file extension      |
      | ["*.cs"]   | Invalid file extension      |
      | ["../cs"]  | Invalid file extension      |
      | [.g.cs]    | Invalid file extension      |
      | .cs        | Invalid YAML                |

  Scenario: Extension overrides cannot refer to a disabled analyzer
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        typescript: [.ts]
      """
    When I preview source indexing
    Then the preview fails with an error containing "analyzer 'typescript', which is not enabled"

  Scenario: An extension override cannot make an unavailable analyzer usable
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - typescript
      fileExtensions:
        typescript: [.ts]
      """
    When I preview source indexing
    Then the preview fails with an error containing "Analyzer 'typescript' is not installed or implemented"

  Scenario: A null extension mapping is rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions: null
      """
    When I preview source indexing
    Then the preview fails with an error containing "fileExtensions must be a mapping"

  Scenario: Duplicate extension override keys are rejected
    Given the repository configuration is:
      """
      version: 1
      analyzers:
        - dotnet-csharp
      fileExtensions:
        dotnet-csharp: [.cs]
        dotnet-csharp: [.csharp]
      """
    When I preview source indexing
    Then the preview fails with an error containing "Invalid YAML"
