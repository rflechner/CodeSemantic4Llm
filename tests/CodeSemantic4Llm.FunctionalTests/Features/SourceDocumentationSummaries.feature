Feature: Prefer existing source documentation when indexing summaries
  As a developer indexing documented C# code
  I want existing XML summaries to be searchable without LLM calls
  So that indexing preserves authored documentation and avoids redundant generation

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      /// <summary>Maintains the navigation catalog.</summary>
      public class Worker
      {
          /// <summary>Resolves JSON routes.</summary>
          public void Resolve() {}
          public void Reset() {}
      }
      """

  Scenario: Documentation is stored and searchable without enabling the LLM
    When I index the checkout
    Then declaration "Worker" has stored summary "Maintains the navigation catalog."
    And declaration "Resolve" has stored summary "Resolves JSON routes."
    And declaration "Reset" has no stored summary
    And summary search "JSON routes" finds "Resolve"
    And the LLM has summarized 0 declarations
    When I index the checkout again
    Then the run analyzes 0 files and reuses 1 files
    And declaration "Resolve" has stored summary "Resolves JSON routes."

  Scenario: A fully documented class requires no LLM calls by default
    Given summary generation is enabled with the default documentation policy
    And indexing source "Worker.cs" is:
      """
      /// <summary>
      /// Maintains the navigation catalog.
      /// </summary>
      public class Worker
      {
          /// <summary>Resolves JSON routes.</summary>
          public void Resolve() {}
      }
      """
    When I index the checkout
    Then declaration "Worker" has stored summary "Maintains the navigation catalog."
    And declaration "Resolve" has stored summary "Resolves JSON routes."
    And the LLM has summarized 0 declarations

  Scenario: The LLM only describes declarations without documentation
    Given summary generation is enabled with the default documentation policy
    When I index the checkout
    Then declaration "Worker" has stored summary "Maintains the navigation catalog."
    And declaration "Resolve" has stored summary "Resolves JSON routes."
    And declaration "Reset" has stored summary "Generated summary for Worker.Reset."
    And the LLM has summarized 1 declarations

  Scenario: Forcing generation replaces documentation and changing the policy restores it
    Given summary generation is enabled with forceGeneration "false"
    And the checkout has a completed index
    When summary generation is enabled with forceGeneration "true"
    And I index the checkout again
    Then the run analyzes 1 files and reuses 0 files
    And declaration "Resolve" has stored summary "Generated summary for Worker.Resolve."
    And declaration "Worker" has stored summary "Generated summary for Worker."
    And the LLM has summarized 4 declarations
    When I index the checkout again
    Then the run analyzes 0 files and reuses 1 files
    And the LLM has summarized 4 declarations
    When summary generation is enabled with forceGeneration "false"
    And I index the checkout again
    Then declaration "Resolve" has stored summary "Resolves JSON routes."
    And the LLM has summarized 5 declarations

  Scenario: Forcing generation does not enable a disabled LLM
    Given summary generation is disabled with forceGeneration "true"
    When I index the checkout
    Then declaration "Resolve" has stored summary "Resolves JSON routes."
    And declaration "Reset" has no stored summary
    And the LLM has summarized 0 declarations

  Scenario Outline: Empty or malformed summaries fall back to generation
    Given summary generation is enabled with forceGeneration "false"
    And I replace indexing source "Worker.cs" with "<source>"
    When I index the checkout
    Then declaration "Worker" has stored summary "Generated summary for Worker."
    And the LLM has summarized 1 declarations

    Examples:
      | source                                                         |
      | /** <summary/> */ public class Worker {}                       |
      | /** <summary> </summary> */ public class Worker {}             |
      | /** <summary>Unclosed documentation. */ public class Worker {} |

  Scenario: Updating and removing documentation updates search without retaining stale text
    Given the checkout has a completed index
    When I replace indexing source "Worker.cs" with "/** <summary>Builds a routing catalog.</summary> */ public class Worker {}"
    And I index the checkout again
    Then declaration "Worker" has stored summary "Builds a routing catalog."
    And summary search "routing catalog" finds "Worker"
    And summary search "JSON routes" finds nothing
    When I replace indexing source "Worker.cs" with "public class Worker {}"
    And I index the checkout again
    Then declaration "Worker" has no stored summary
    And summary search "routing catalog" finds nothing

  Scenario: Adding documentation replaces an earlier generated summary without a new LLM call
    Given summary generation is enabled with forceGeneration "false"
    And I replace indexing source "Worker.cs" with "public class Worker {}"
    And the checkout has a completed index
    When I replace indexing source "Worker.cs" with "/** <summary>Maintains the catalog.</summary> */ public class Worker {}"
    And I index the checkout again
    Then declaration "Worker" has stored summary "Maintains the catalog."
    And the LLM has summarized 1 declarations

  Scenario: Branches retain their own documentation
    Given the checkout has a completed index
    When I switch the indexing checkout to a new branch "alternate"
    And I replace indexing source "Worker.cs" with "/** <summary>Maintains another catalog.</summary> */ public class Worker {}"
    And I index the checkout again
    Then declaration "Worker" has stored summary "Maintains another catalog."
    And branch "main" retains summary "Maintains the navigation catalog." for "Worker"
