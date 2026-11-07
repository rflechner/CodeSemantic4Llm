Feature: Debounced, syntax-safe source watching
  As a developer editing a large C# repository
  I want the live index to wait for a quiet period and syntactically complete files
  So that coding agents do not navigate half-written declarations

  Scenario: An incomplete edit keeps the previously published index
    Given a C# repository with a published code index
    And I watch the repository with a short debounce
    When I save an incomplete C# declaration
    Then the watcher reports a syntax error without replacing the published index
    When I finish the C# declaration
    Then the watcher publishes the corrected declaration

  Scenario: Repeated saves reset the quiet period
    Given a C# repository with a published code index
    And I watch the repository with a short debounce
    When I save two valid C# edits within the quiet period
    Then the watcher publishes the final declaration but not the intermediate one

  Scenario: Successful reconciliation refreshes shared summaries for commit
    Given a C# repository with a published code index
    And I watch the repository with a short debounce
    When I finish the C# declaration
    Then the watcher publishes the corrected declaration
    And the watcher refreshes the shared summary for commit

  Scenario: Rename and deletion remove stale source locations
    Given a C# repository with a published code index
    And I watch the repository with a short debounce
    When I rename an indexed source file
    Then the watcher updates the declaration's source path
    When I delete the renamed source file
    Then the watcher removes its declaration
