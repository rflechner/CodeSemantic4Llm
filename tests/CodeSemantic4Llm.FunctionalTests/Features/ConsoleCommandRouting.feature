Feature: Route console commands without interfering with MCP
  As a local operator
  I want generated command help and strict argument parsing
  So that invalid invocations fail promptly and MCP stdio remains usable

  Scenario Outline: Top-level help describes the console commands and default server mode
    When I request application help with "<option>"
    Then application help lists init, preview, index, dashboard and the default MCP server

    Examples:
      | option |
      | --help |
      | -h     |

  Scenario Outline: Unknown commands and options fail without starting MCP
    When I invoke the application with "<argument>"
    Then the application reports a console error containing "<argument>"

    Examples:
      | argument   |
      | unexpected |
      | --unknown  |
      | [red]bad   |

  Scenario: Starting without arguments preserves the MCP protocol on stdout
    When I start the application without arguments and initialize an MCP session
    Then the first stdout line is a successful MCP initialization response
