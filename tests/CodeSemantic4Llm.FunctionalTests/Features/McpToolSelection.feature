Feature: Select the MCP tools a repository advertises
  As the local server operator
  I want each checkout's YAML to choose which MCP tools its server exposes
  So that I can hide tools my agent should not use without changing the server

  Scenario: Initialization offers every tool for the operator to narrow
    When I initialize a project from the console
    Then the generated configuration selects every tool
    And the generated configuration explains how to hide a tool

  Scenario: A configuration without a tools section advertises every granted tool
    Given a project that selects every tool
    When I serve it with editing "on" and indexing "on"
    And I list its MCP tools
    Then MCP advertises exactly every tool of the catalog

  Scenario: Only the selected tools are advertised
    Given a project that selects these tools:
      | tool            |
      | get_server_info |
      | find_symbols    |
      | get_method_body |
    When I serve it with editing "on" and indexing "on"
    And I list its MCP tools
    Then MCP advertises exactly these tools:
      | tool            |
      | get_server_info |
      | find_symbols    |
      | get_method_body |

  Scenario: The selection order and letter case do not change what is advertised
    Given a project that selects these tools:
      | tool            |
      | GET_METHOD_BODY |
      | get_server_info |
      | get_method_body |
    When I serve it with editing "off" and indexing "off"
    And I list its MCP tools
    Then MCP advertises exactly these tools:
      | tool            |
      | get_server_info |
      | get_method_body |

  Scenario: A selected tool keeps answering
    Given a project that selects these tools:
      | tool            |
      | get_server_info |
    When I serve it with editing "off" and indexing "off"
    And I call the "get_server_info" tool
    Then the tool call succeeds

  Scenario: A deselected tool is refused when a client calls it anyway
    Given a project that selects these tools:
      | tool            |
      | get_server_info |
    When I serve it with editing "off" and indexing "off"
    And I call the "get_file_diagnostics" tool
    Then the tool call is refused with "not enabled for this project"

  Scenario Outline: A selection never grants a capability the operator withheld
    Given a project that selects these tools:
      | tool            |
      | get_server_info |
      | <tool>          |
    When I serve it with editing "off" and indexing "off"
    And I list its MCP tools
    Then MCP advertises exactly these tools:
      | tool            |
      | get_server_info |
    And calling "<tool>" is refused

    Examples:
      | tool                         |
      | replace_method_body          |
      | replace_method               |
      | get_repository_configuration |
      | get_repository_index_status  |

  Scenario Outline: An unusable tools section is reported to the operator
    Given a project with this tools section:
      """
      <section>
      """
    When I preview it from the console
    Then the console reports an error containing "<error>"

    Examples:
      | section                       | error                           |
      | tools: []                     | must list at least one MCP tool |
      | tools:                        | must list at least one MCP tool |
      | tools: [find_symbol]          | Unknown MCP tool 'find_symbol'  |
      | tools: [get_server_info, ""]  | cannot be empty                 |
      | tools: get_server_info        | Invalid YAML                    |

  Scenario: An unreadable selection leaves the startup capabilities as the only restriction
    Given a project with this tools section:
      """
      tools: [find_symbol]
      """
    When I serve it with editing "off" and indexing "off"
    And I list its MCP tools
    Then MCP advertises exactly every read-only tool of the catalog
