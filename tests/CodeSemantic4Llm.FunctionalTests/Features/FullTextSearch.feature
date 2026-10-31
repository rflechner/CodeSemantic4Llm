Feature: Search code from saved summaries and symbol names
  As a coding agent navigating CodeSemantic4Llm
  I want locally ranked matches for what a declaration does
  So that I do not have to read every source file or call an LLM during search

  Background:
    Given a Git checkout configured for persistent indexing
    And the indexing checkout contains these sources:
      | path                 | source                                                                          |
      | src/CodeNavigator.cs | namespace Demo; class CodeNavigator { void FindUsages() {} void RefreshIndex() {} } |

  Scenario: The default MCP exposes keyword search without indexing details
    Given the checkout has a completed index
    When default MCP searches indexed code for "FindUsages"
    Then full-text search returns method "FindUsages" from "src/CodeNavigator.cs"
    And its response does not expose snapshot metadata

  Scenario: Explicit indexing exposure returns snapshot identity alongside matches
    Given the checkout has a completed index
    When detailed MCP searches indexed code for "RefreshIndex"
    Then full-text search returns method "RefreshIndex" from "src/CodeNavigator.cs"
    And its response includes published snapshot metadata

  Scenario: An absent index is never created by a search request
    When default MCP searches indexed code for "FindUsages"
    Then full-text search reports an error
    And no index database was created
