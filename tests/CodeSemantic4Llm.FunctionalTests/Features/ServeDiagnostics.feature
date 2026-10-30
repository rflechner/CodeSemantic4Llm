Feature: Diagnose existing source through MCP
  As a coding agent connected to a read-only server
  I want current syntax errors without providing source code or an index snapshot
  So that I can locate errors in a file or class immediately after editing

  Scenario: Diagnose a file and class over stdio without creating an index
    Given two configured MCP project directories
    When I diagnose a malformed file and its class through read-only MCP
    Then MCP returns precise current-file and class syntax errors
    And MCP startup creates no index cache
