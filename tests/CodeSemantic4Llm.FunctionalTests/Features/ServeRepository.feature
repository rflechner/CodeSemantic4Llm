Feature: Select the MCP repository from the serve command
  As an operator configuring an MCP client
  I want to pass the project directory as a server argument
  So that each server instance accesses the intended repository

  Scenario Outline: Select a project with an absolute or relative path
    Given two configured MCP project directories
    When I serve the selected project using an "<path>" path with "<environment>" environment configuration
    Then MCP previews the project selected by the argument
    And MCP startup creates no index cache

    Examples:
      | path     | environment |
      | absolute | absent      |
      | relative | absent      |
      | absolute | conflicting |
      | relative | conflicting |

  Scenario Outline: Keep environment configuration when no path is supplied
    Given two configured MCP project directories
    When I start MCP using "<invocation>" and the environment repository
    Then MCP previews the project selected by the environment
    And MCP startup creates no index cache

    Examples:
      | invocation |
      | serve      |
      | no command |

  Scenario: Serve help describes the optional project argument
    When I request serve command help
    Then serve help describes the repository argument and environment fallback

  Scenario Outline: Invalid serve arguments fail before MCP startup
    When I invoke serve with "<problem>"
    Then serve reports an error containing "<message>"

    Examples:
      | problem             | message                        |
      | empty path          | repository path cannot be empty |
      | whitespace path     | repository path cannot be empty |
      | nonexistent path    | does not exist                 |
      | file path           | does not exist                 |
      | extra argument      | extra                          |
      | unknown option      | Unknown option                 |
