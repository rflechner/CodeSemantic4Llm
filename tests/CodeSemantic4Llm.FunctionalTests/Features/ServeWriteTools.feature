Feature: Explicitly opt in to source editing when starting MCP
  As the local server operator
  I want MCP tools to be read-only unless I pass an explicit flag
  So that an agent cannot enable file edits by itself

  Background:
    Given two configured MCP project directories

  Scenario Outline: Advertise edits only when the operator enables writing
    When I list MCP tools with "<mode>" server startup
    Then MCP advertises the method body reader
    And MCP writing tools are "<state>"
    And MCP indexing details are "<details>"

    Examples:
      | mode           | state     | details |
      | read-only      | absent    | hidden  |
      | write-enabled  | available | hidden  |
      | default        | absent    | hidden  |
      | indexing       | absent    | exposed |
      | indexing-write | available | exposed |
