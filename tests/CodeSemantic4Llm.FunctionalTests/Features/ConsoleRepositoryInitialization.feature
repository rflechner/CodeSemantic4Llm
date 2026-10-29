Feature: Initialize repository configuration from a terminal
  As a developer preparing a checkout for semantic discovery
  I want to create its YAML with chosen analyzers and a description language
  So that I can preview the repository without writing configuration by hand

  Background:
    Given an existing target repository for initialization

  Scenario Outline: Initialize a repository with the default settings
    When I initialize the target repository using an "<path>" path
    Then repository initialization succeeds
    And the generated configuration uses language "en" and the default C# settings
    And the initialized repository can be previewed with built-in exclusions

    Examples:
      | path     |
      | absolute |
      | relative |

  Scenario Outline: Choose and normalize the semantic description language
    When I initialize the target repository with language "<language>"
    Then repository initialization succeeds
    And the generated configuration uses language "<normalized>" and the default C# settings

    Examples:
      | language   | normalized |
      | fr         | fr         |
      | FR-ch      | fr-CH      |
      | zh-hant-tw | zh-Hant-TW |

  Scenario Outline: Combine an explicit analyzer with a language in either order
    When I run repository initialization with these arguments:
      | argument     |
      | {repository} |
      | <first>      |
      | <firstValue> |
      | <last>       |
      | <lastValue>  |
    Then repository initialization succeeds
    And the generated configuration uses language "fr-CH" and the default C# settings

    Examples:
      | first      | firstValue    | last       | lastValue     |
      | --analyzer | DOTNET-CSHARP | --lang     | FR-ch         |
      | --lang     | FR-ch         | --analyzer | DOTNET-CSHARP |

  Scenario: Repeated analyzer options preserve the order of first occurrence
    Given a test-only analyzer "test-secondary" is available for initialization
    When I run repository initialization with these arguments:
      | argument       |
      | {repository}   |
      | --analyzer     |
      | test-secondary |
      | --lang         |
      | fr             |
      | --analyzer     |
      | dotnet-csharp  |
      | --analyzer     |
      | TEST-SECONDARY |
    Then repository initialization succeeds
    And the generated configuration selects these analyzers in order:
      | analyzer       |
      | test-secondary |
      | dotnet-csharp  |
    And the generated configuration language is "fr"

  Scenario: An explicit selection replaces the default analyzer
    Given the default C# analyzer is unavailable for initialization
    And a test-only analyzer "test-secondary" is available for initialization
    When I run repository initialization with these arguments:
      | argument       |
      | {repository}   |
      | --analyzer     |
      | test-secondary |
    Then repository initialization succeeds
    And the generated configuration selects these analyzers in order:
      | analyzer       |
      | test-secondary |
    And the generated configuration language is "en"

  Scenario Outline: Reject unavailable or empty analyzers even after an available selection
    When I run repository initialization with these arguments:
      | argument      |
      | {repository}  |
      | --analyzer    |
      | dotnet-csharp |
      | --analyzer    |
      | <analyzer>    |
    Then repository initialization fails with an error containing "<error>"
    And initialization leaves no configuration or temporary files

    Examples:
      | analyzer   | error                                                 |
      | unknown    | Analyzer 'unknown' is not installed or implemented     |
      | typescript | Analyzer 'typescript' is not installed or implemented |
      |            | non-empty analyzer ID is required                     |

  Scenario Outline: Reject invalid languages without creating a configuration
    When I initialize the target repository with language "<language>"
    Then repository initialization fails with an error containing "<error>"
    And initialization leaves no configuration or temporary files

    Examples:
      | language | error                              |
      | english  | Invalid semantic indexing language |
      | fr_CH    | Invalid semantic indexing language |
      |          | language cannot be empty           |

  Scenario: Preserve an existing configuration byte for byte
    Given the target repository already has a custom configuration
    When I initialize the target repository with language "fr"
    Then repository initialization fails with an error containing "already exists"
    And the original target configuration is unchanged

  Scenario: Refuse a configuration path that is a symbolic link
    Given the target configuration links to an external file
    When I initialize the target repository with language "fr"
    Then repository initialization fails with an error containing "already exists"
    And the external configuration and its symbolic link are unchanged

  Scenario: Refuse a directory at the configuration path
    Given the target configuration path is a directory
    When I initialize the target repository using an "absolute" path
    Then repository initialization fails with an error containing "already exists"
    And the directory at the configuration path is unchanged

  Scenario: Do not create a missing repository directory
    Given the target repository directory does not exist
    When I initialize the target repository using an "absolute" path
    Then repository initialization fails with an error containing "does not exist or is not a directory"
    And the target repository directory still does not exist

  Scenario: Reject a file supplied as the repository path
    Given the target repository path is a file
    When I initialize the target repository using an "absolute" path
    Then repository initialization fails with an error containing "does not exist or is not a directory"
    And the file at the target repository path is unchanged

  Scenario Outline: Invalid command arguments report an error without creating YAML
    When I run repository initialization with these arguments:
      | argument     |
      | <repository> |
      | <option>     |
      | <value>      |
    Then repository initialization fails with an error containing "<error>"
    And initialization leaves no configuration or temporary files

    Examples:
      | repository   | option    | value | error                            |
      |              | --lang    | fr    | Expected a repository path       |
      | --lang       | fr        |       | Expected a repository path       |
      | {repository} | --unknown | fr    | Unknown option '--unknown'       |
      | {repository} | --lang    |       | language cannot be empty         |

  Scenario: A missing language argument is rejected
    When I run repository initialization with these arguments:
      | argument     |
      | {repository} |
      | --lang       |
    Then repository initialization fails with an error containing "Option 'lang' is defined but no value has been provided"
    And initialization leaves no configuration or temporary files

  Scenario: A missing analyzer argument is rejected
    When I run repository initialization with these arguments:
      | argument     |
      | {repository} |
      | --analyzer   |
    Then repository initialization fails with an error containing "Option 'analyzer' is defined but no value has been provided"
    And initialization leaves no configuration or temporary files

  Scenario Outline: An option cannot consume another option as its value
    When I run repository initialization with these arguments:
      | argument     |
      | {repository} |
      | <option>     |
      | <next>       |
      | <value>      |
    Then repository initialization fails with an error containing "no value has been provided"
    And initialization leaves no configuration or temporary files

    Examples:
      | option     | next       | value         |
      | --analyzer | --lang     | fr            |
      | --analyzer | --analyzer | dotnet-csharp |
      | --lang     | --analyzer | dotnet-csharp |

  Scenario: Repeating the language option is rejected
    When I run repository initialization with these arguments:
      | argument      |
      | {repository}  |
      | --lang        |
      | fr            |
      | --analyzer    |
      | dotnet-csharp |
      | --lang        |
      | en            |
    Then repository initialization fails with an error containing "--lang may only be specified once"
    And initialization leaves no configuration or temporary files

  Scenario: Unexpected positional arguments are rejected
    When I run repository initialization with these arguments:
      | argument      |
      | {repository}  |
      | --analyzer    |
      | dotnet-csharp |
      | extra         |
    Then repository initialization fails with an error containing "Could not match 'extra' with an argument"
    And initialization leaves no configuration or temporary files

  Scenario: A missing repository argument is rejected
    When I run repository initialization without arguments
    Then repository initialization fails with an error containing "missing required argument 'repository-path'"
    And initialization leaves no configuration or temporary files

  Scenario Outline: Help is available without creating a configuration
    When I run repository initialization with these arguments:
      | argument |
      | <help>   |
    Then initialization help succeeds
    And initialization leaves no configuration or temporary files

    Examples:
      | help   |
      | --help |
      | -h     |

  Scenario: Cancellation leaves the repository unconfigured
    When I cancel repository initialization before it starts
    Then repository initialization is canceled
    And initialization leaves no configuration or temporary files

  Scenario: An unavailable default analyzer fails explicitly
    Given the default C# analyzer is unavailable for initialization
    When I initialize the target repository using an "absolute" path
    Then repository initialization fails with an error containing "not installed or implemented"
    And initialization leaves no configuration or temporary files

  Scenario: Options can precede the repository path and use equals syntax
    When I run repository initialization with these arguments:
      | argument                   |
      | --lang=FR-ch                |
      | --analyzer=DOTNET-CSHARP     |
      | {repository}               |
      | --analyzer=dotnet-csharp     |
    Then repository initialization succeeds
    And the generated configuration uses language "fr-CH" and the default C# settings

  Scenario: Unbound arguments after the separator do not bypass validation
    When I run repository initialization with these arguments:
      | argument     |
      | {repository} |
      | --           |
      | --ignored    |
    Then repository initialization fails with an error containing "--ignored"
    And initialization leaves no configuration or temporary files
