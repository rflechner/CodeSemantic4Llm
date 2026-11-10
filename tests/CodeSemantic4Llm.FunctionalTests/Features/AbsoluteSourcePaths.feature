Feature: Expose directly usable source paths over MCP
  As a coding harness
  I want absolute paths alongside relative source paths
  So that I can open files without reconstructing their repository root

  Scenario: Navigation, source reads, diagnostics, writes and previews include absolute paths
    Given a Git checkout configured for persistent indexing
    And indexing source "nested folder/Worker.cs" is:
      """
      class Worker
      {
          public int Run() => 1;
          public int Call() => Run();
      }
      """
    And the checkout has a completed index
    When MCP inspects and edits the nested Worker source
    Then every returned relative source path has its matching absolute path

  Scenario Outline: A repository returns only the selected source path fields
    Given a Git checkout configured for persistent indexing
    And indexing source "nested folder/Worker.cs" is:
      """
      class Worker
      {
          public int Run() => 1;
          public int Call() => Run();
      }
      """
    And the checkout has a completed index
    And MCP source paths are configured as "<format>"
    When MCP inspects and edits the nested Worker source
    Then every returned source path uses only "<format>" fields

    Examples:
      | format   |
      | relative |
      | absolute |
      | both     |

  Scenario: Path format changes apply to the next call without rebuilding the index
    Given a Git checkout configured for persistent indexing
    And indexing source "nested folder/Worker.cs" is:
      """
      class Worker { public int Run() => 1; }
      """
    And the checkout has a completed index
    When MCP changes path formats between calls without restarting
