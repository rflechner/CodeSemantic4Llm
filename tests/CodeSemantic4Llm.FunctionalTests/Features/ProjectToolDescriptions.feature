Feature: Identify each MCP tool's project
  As a coding agent connected to several repository servers
  I want tool descriptions to identify their project
  So that I can choose the instance that owns the requested code

  Scenario Outline: Every exposed tool identifies the configured project
    Given a tool discovery project named 'TinyHarness'
    When I discover its MCP tools with editing "<editing>" and indexing "<indexing>"
    Then every discovered tool description starts with "In project TinyHarness, "
    And the file diagnostic description keeps its usage guidance
    And discovery keeps editing "<editing>" and indexing "<indexing>" exposure
    And repeating discovery returns identical tools
    And tool discovery creates no index cache

    Examples:
      | editing | indexing |
      | off     | off      |
      | on      | off      |
      | off     | on       |
      | on      | on       |

  Scenario: Display names preserve spaces, punctuation and Unicode
    Given a tool discovery project named 'Équipe "C#": API #1'
    When I discover its MCP tools with editing "off" and indexing "off"
    Then every discovered tool description starts with 'In project Équipe "C#": API #1, '

  Scenario Outline: Discovery remains available without a configured display name
    Given a tool discovery project with "<configuration>" configuration
    When I discover its MCP tools with editing "off" and indexing "off"
    Then every discovered tool description starts with "In project checkout, "
    And the file diagnostic description keeps its usage guidance
    And tool discovery creates no index cache

    Examples:
      | configuration |
      | unnamed       |
      | null name     |
      | missing       |
      | invalid       |

  Scenario: Connected project instances advertise distinct descriptions
    Given a tool discovery project named 'TinyHarness'
    When I discover tools from this project and another project named "OtherProject"
    Then every discovered tool description starts with "In project TinyHarness, "
    And the other server's descriptions start with "In project OtherProject, "
    And repeating discovery returns identical tools

  Scenario: A refreshed tool list uses the updated display name
    Given a tool discovery project named 'TinyHarness'
    When I discover its MCP tools with editing "off" and indexing "off"
    And I change the discovery project name to "RenamedProject" and refresh its tools
    Then every discovered tool description starts with "In project RenamedProject, "
