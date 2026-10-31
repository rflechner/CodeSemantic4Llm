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
