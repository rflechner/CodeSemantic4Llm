@dashboard-search
Feature: Try code intent search in the dashboard
  As an operator evaluating code navigation for a coding agent
  I want to search a saved branch with the same ranking as MCP search_code
  So that I can inspect relevant declarations and summaries before using an LLM

  Background:
    Given the dashboard cache contains these declarations:
      | project | branch  | kind   | name            | summary                                                       |
      | alpha   | develop | method | PublishSnapshot | Publishes a **branch snapshot** and replaces its predecessor.  |
      | alpha   | develop | class  | IndexRepository | Indexes source files into a branch snapshot.                   |
      | alpha   | develop | method | FindUsages      |                                                               |
      | alpha   | main    | method | ArchiveSnapshot | Archives an obsolete snapshot.                                |
      | beta    | develop | class  | RestoreSnapshot | Restores a snapshot from another repository.                   |

  Scenario: Intent search preserves MCP ranking and complete declaration metadata
    When the dashboard searches "alpha" branch "develop" for "Where do we publish a branch snapshot?" with kind "" and limit 10
    Then the dashboard search matches are "PublishSnapshot, IndexRepository" in that order
    And the dashboard search matches the MCP search use case
    And the dashboard search is complete

  Scenario: A bounded search reports additional matches
    When the dashboard searches "alpha" branch "develop" for "publish branch snapshot" with kind "" and limit 1
    Then the dashboard search matches are "PublishSnapshot" in that order
    And the dashboard search reports more matches

  Scenario: The kind filter applies before the result limit
    When the dashboard searches "alpha" branch "develop" for "branch snapshot" with kind "class" and limit 1
    Then the dashboard search matches are "IndexRepository" in that order
    And the dashboard search is complete

  Scenario: Identifiers remain searchable without a generated summary
    When the dashboard searches "alpha" branch "develop" for "FindUsages" with kind "method" and limit 10
    Then the dashboard search matches are "FindUsages" in that order
    And the first dashboard match has no summary

  Scenario Outline: Only the selected project and branch contribute matches
    Given the original dashboard checkouts are unavailable
    When the dashboard searches "<project>" branch "<branch>" for "snapshot" with kind "" and limit 10
    Then the dashboard search matches are "<names>" in that order
    And the dashboard search identifies the published snapshot for "<project>" branch "<branch>"

    Examples:
      | project | branch  | names                            |
      | alpha   | develop | IndexRepository, PublishSnapshot |
      | alpha   | main    | ArchiveSnapshot                  |
      | beta    | develop | RestoreSnapshot                  |

  Scenario: An unrelated intent produces an empty complete result
    When the dashboard searches "alpha" branch "develop" for "astronomy" with kind "" and limit 10
    Then the dashboard search matches are "" in that order
    And the dashboard search is complete

  Scenario Outline: Invalid queries cannot be interpreted as an unbounded browse request
    When the dashboard searches "alpha" branch "develop" for "<query>" with kind "" and limit <limit>
    Then the dashboard search rejects the invalid request

    Examples:
      | query        | limit |
      |              | 10    |
      | where is the | 10    |
      | snapshot     | 0     |
      | snapshot     | 201   |
