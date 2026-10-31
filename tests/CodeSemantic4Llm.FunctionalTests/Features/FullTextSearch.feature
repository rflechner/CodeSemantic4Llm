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
    And full-text search locations include matching absolute source paths

  Scenario: Indexing administration preserves the compact code search response
    Given the checkout has a completed index
    When MCP with indexing administration searches indexed code for "RefreshIndex"
    Then full-text search returns method "RefreshIndex" from "src/CodeNavigator.cs"
    And its response does not expose snapshot metadata
    And full-text search locations include matching absolute source paths

  Scenario: An absent index is never created by a search request
    When default MCP searches indexed code for "FindUsages"
    Then full-text search reports an error
    And no index database was created

  Scenario Outline: A model sending the text null does not accidentally filter out every declaration
    Given the checkout has saved JSON navigation summaries
    When MCP in "<mode>" mode searches for "<query>" with the text null as its kind
    Then full-text search returns method "Load" from "src/NavigationIndex.cs"
    And full-text search matches the unfiltered dashboard search for "<query>"
    And its response does not expose snapshot metadata

    Examples:
      | mode     | query                                  |
      | default  | TinyHarness index JSON pour navigation |
      | default  | gestion d'un index de navigation JSON  |
      | indexing | TinyHarness index JSON pour navigation |
      | indexing | gestion d'un index de navigation JSON  |

  Scenario: Optional kind representations preserve unrestricted search and real filters
    Given the checkout has saved JSON navigation summaries
    When MCP searches JSON navigation with these kind arguments:
      | argument | expected kinds |
      | omitted  | class, method  |
      | null     | class, method  |
      | "null"   | class, method  |
      | " NULL " | class, method  |
      | ""       | class, method  |
      | " "      | class, method  |
      | "method" | method         |
      | "class"  | class          |
      | "unknown" |                |
    Then every search honors the intended kind filter
