Feature: Preview resolved usages when MCP search returns a type
  As a coding agent finding a type declaration
  I want a small sample of its actual references in the same response
  So that I can navigate its callers without another MCP request

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario Outline: Exact-name and ranked searches return resolved type references
    Given the indexing checkout contains these sources:
      | path          | source                                                               |
      | src/Worker.cs | namespace Demo; public class Worker {}                               |
      | src/Caller.cs | namespace Demo; class Caller { Worker Field = new Worker(); }         |
    When I index the checkout
    And MCP "<tool>" searches for type "Worker"
    Then MCP search returns 2 references to "Worker" in "src/Caller.cs"
    And the MCP type usage preview is available and complete
    And the MCP enriched search hides snapshot metadata

    Examples:
      | tool         |
      | find_symbols |
      | search_code  |

  Scenario: Type search returns a bounded preview and exposes truncation
    Given the indexing checkout contains these sources:
      | path          | source                                                                                                       |
      | src/Worker.cs | namespace Demo; public class Worker {}                                                                       |
      | src/Caller.cs | namespace Demo; class Caller { Worker A; Worker B; Worker C; Worker D; Worker E; Worker F; Worker G; }      |
    When I index the checkout
    And MCP "search_code" searches for type "Worker"
    Then MCP search returns 5 references to "Worker" in "src/Caller.cs"
    And the MCP type usage preview has more results

  Scenario: Partial folder indexes do not falsely claim a type has no references
    Given the indexing checkout contains these sources:
      | path             | source                                             |
      | src/Worker.cs    | namespace Demo; public class Worker {}             |
      | callers/Caller.cs| namespace Demo; class Caller { Worker Field; }     |
    When I incrementally index folder "src"
    And MCP "find_symbols" searches for type "Worker"
    Then the MCP type usage preview is unavailable
    And the MCP enriched search hides snapshot metadata

  Scenario Outline: Method-only searches keep the original compact response
    Given indexing source "Worker.cs" is:
      """
      class Worker { public void Run() {} }
      """
    When I index the checkout
    And MCP "<tool>" searches for method "Run"
    Then MCP search does not contain type usage previews

    Examples:
      | tool         |
      | find_symbols |
      | search_code  |

  Scenario: Equal simple names are matched by declaration location, not text
    Given the indexing checkout contains these sources:
      | path         | source                                                            |
      | src/Left.cs  | namespace Left; class Worker {} class Client { Worker Value; }     |
      | src/Right.cs | namespace Right; class Worker {} class Client { Worker Value; }    |
    When I index the checkout
    And MCP "find_symbols" searches for type "Worker"
    Then both Worker types have separate usage previews
