Feature: Edit only one indexed C# method body through MCP
  As a coding agent using the operator-selected checkout
  I want to edit a precise method without rewriting its entire file
  So that surrounding code and unexpected newer changes remain untouched

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Replace only the selected overload and report that the index is stale
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run() { return 1; }
          public int Run(int value) { return value + 1; }
      }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    Then the selected body contains "return 1;"
    When I replace the selected method body with "{ return 2; }"
    Then the method body replacement succeeds and requires reindexing
    And the source file contains "public int Run() { return 2; }"
    And the source file contains "public int Run(int value) { return value + 1; }"
    When I attempt to read the selected method body again
    Then the method body request fails with "source file has changed since indexing"

  Scenario: Editing requires explicit server permission even with valid index data
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    When I attempt to replace its body with writing disabled
    Then the method body request fails with "writes are disabled"
    And the source file contains "return 1;"

  Scenario: An external modification after reading is never overwritten
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I replace indexing source "Worker.cs" with "class Worker { public int Run() { return 10; } }"
    When I attempt to replace its body with "{ return 2; }"
    Then the method body request fails with "source file has changed since indexing"
    And the source file contains "return 10;"

  Scenario: An invalid C# body is rejected without changing the source
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    When I attempt to replace its body with "return 2"
    Then the method body request fails with "syntactically valid"
    And the source file contains "return 1;"

  Scenario: Validation and editing accept statements without outer braces
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } public int Keep() => 7; }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I validate proposed method body "var value = 21; return value * 2;"
    Then method syntax diagnostics are valid
    And the source file contains "return 1;"
    When I replace the selected method body with "var value = 21; return value * 2;"
    Then the method body replacement succeeds and requires reindexing
    And the source file contains "var value = 21; return value * 2;"
    And the source file contains "public int Keep() => 7;"

  Scenario: An earlier index generation cannot authorize a write
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I index the checkout again
    When I attempt to replace its body with "{ return 2; }"
    Then the method body request fails with "snapshot changed"
    And the source file contains "return 1;"

  Scenario: The method body reader paginates source text without reading whole files into the MCP context
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 12345; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    When I read the selected method body in pages of 5 characters
    Then the complete selected body contains "return 12345;"
    And the method body was read in multiple pages

  Scenario: Expression-bodied methods can become block-bodied without rewriting their signatures
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run(int value) => value + 1; }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    Then the selected method has an expression body
    When I replace the selected method body with "{ return value * 2; }"
    Then the method body replacement succeeds and requires reindexing
    And the source file contains "Run(int value) { return value * 2; }"
