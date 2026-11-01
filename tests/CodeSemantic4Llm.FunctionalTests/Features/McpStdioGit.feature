Feature: Keep Git queries independent of the MCP connection
  As a coding agent connected over stdio
  I want repository queries to finish while my connection stays open
  So that Git subprocesses cannot block the next MCP response

  Scenario Outline: Consecutive method reads complete on one open connection
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run() => 1;
      }
      """
    When "<mode>" MCP reads Run twice without closing the connection
    Then both MCP method reads return "=> 1;"
    And no index database was created

    Examples:
      | mode     |
      | default  |
      | indexing |
