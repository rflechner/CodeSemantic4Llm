Feature: Keep storage lifecycle out of the default model interface
  As a coding agent
  I want to navigate and check code using source symbols
  So that I do not need to manage indexing or snapshot metadata

  Background:
    Given a Git checkout configured for persistent indexing
    And indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run() => 1;
          public int Call() => Run();
      }
      """

  Scenario: Navigate and validate with source addresses over MCP
    Given the checkout has a completed index
    When default MCP navigates Worker and validates Run
    Then code tool replies contain no indexing details

  Scenario Outline: Both method body formats share validation and editing behavior
    Given the checkout has a completed index
    When default MCP validates and replaces Run with "<body>"
    Then code tool replies contain no indexing details
    And the default MCP write changes the method body

    Examples:
      | body           |
      | { return 42; } |
      | return 42;     |

  Scenario: A relative replacement body inherits the method indentation
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          public async Task<int> Run()
          {
              return 1;
          }

          public int Keep() => 7;
      }
      """
    When default MCP replaces Run with:
      """
      // Start execution.
      // Preserve the current context.
      {
          var previous = GetContext();

          try {
              return await Execute();
          }
          finally {
              RestoreContext(previous);
          }
      }
      """
    Then source file "Worker.cs" is exactly:
      """
      class Worker
      {
          public async Task<int> Run()
          // Start execution.
          // Preserve the current context.
          {
              var previous = GetContext();

              try {
                  return await Execute();
              }
              finally {
                  RestoreContext(previous);
              }
          }

          public int Keep() => 7;
      }
      """

  Scenario Outline: Direct edits select current source independently of earlier reads
    Given the checkout has a completed index
    When default MCP edits the current method after "<change>"
    Then the default MCP write changes the method body
    And code tool replies contain no indexing details

    Examples:
      | change       |
      | branch switch|
      | source edit  |

  Scenario: Missing navigation data is reported without teaching indexing to the model
    When default MCP searches an unprepared repository
    Then default MCP refuses the stale selection without exposing indexing details
    And no index database was created

  Scenario: Indexing exposure adds administration without changing code tool contracts
    Given the checkout has a completed index
    When MCP searches with indexing exposure enabled
    Then only the administration replies include indexing metadata

  Scenario Outline: Read all syntax diagnostics without supplying file metadata
    Given the checkout has a completed index
    When default MCP reads all "<scope>" diagnostics without file metadata
    Then code tool replies contain no indexing details

    Examples:
      | scope    |
      | file     |
      | type     |
      | method   |
      | proposed |

  Scenario: Starting another analysis cannot replace an older continuation's file version
    When default MCP overlaps diagnostic reads across a source edit
    Then default MCP refuses the changed diagnostic page
    And code tool replies contain no indexing details

  Scenario: A diagnostic continuation cannot select another scope
    When default MCP uses a file diagnostic continuation for a type
    Then default MCP refuses the changed diagnostic page

  Scenario: Validation can be followed by a direct edit without reading
    When default MCP validates a method then edits it without reading
    Then the default MCP write changes the method body
    And code tool replies contain no indexing details
    And no index database was created

  Scenario Outline: Body edits do not depend on which reading tool was used
    When "<mode>" MCP replaces Run after "<read>"
    Then the default MCP write changes the method body
    And code tool replies contain no indexing details
    And no index database was created

    Examples:
      | mode     | read            |
      | default  | no read         |
      | default  | get_method      |
      | default  | get_method_body |
      | indexing | no read         |
      | indexing | get_method      |
      | indexing | get_method_body |

  Scenario: Refreshing navigation data does not invalidate a method read
    Given the checkout has a completed index
    When default MCP reads and edits Run across a refresh
    Then code tool replies contain no indexing details
    And the default MCP write changes the method body

  Scenario: Method editing needs neither navigation data nor temporary identities
    When default MCP edits Run twice across a server restart without navigation
    Then code tool replies contain no indexing details
    And no index database was created

  Scenario: Overloaded methods require an explicit source position
    When default MCP selects overloaded methods by their current source position
    Then code tool replies contain no indexing details
    And no index database was created

  Scenario: Navigation pages cannot cross source generations
    Given the checkout has a completed index
    When default MCP continues navigation after a refresh
    Then default MCP asks to restart pagination

  Scenario: Overlapping method reads cannot replace the source version of an older page
    When default MCP overlaps method body pages across a source edit
    Then default MCP asks to restart pagination
    And code tool replies contain no indexing details
    And no index database was created
