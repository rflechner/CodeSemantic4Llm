Feature: Share generated descriptions through Git
  Developers keep a local SQLite search index and exchange derived Markdown summaries.
  Reusing compatible descriptions must not require a second developer to contact the LLM.

  Scenario: A fresh local database imports a colleague's descriptions
    Given a checkout with exported shared summaries
    When another developer indexes it with a fresh local database
    Then the shared descriptions are searchable without additional LLM calls

  Scenario: A newly pulled description replaces the local search text
    Given a checkout with exported shared summaries
    When another developer indexes it with a fresh local database
    And a colleague's updated Markdown description arrives
    Then search uses the updated description without additional LLM calls
