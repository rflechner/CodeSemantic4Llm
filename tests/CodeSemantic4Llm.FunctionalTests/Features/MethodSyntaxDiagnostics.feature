Feature: Get precise Roslyn syntax diagnostics for C# methods
  As a coding agent changing a method
  I want bounded syntax errors before and after an edit
  So that I can correct mistakes without writing or compiling whole files

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Preview a malformed method body without changing the source
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I validate proposed method body "{ return 2 }"
    Then method syntax diagnostics are invalid
    And method syntax diagnostics include "CS1002"
    And the source file contains "return 1;"

  Scenario: A syntactically valid candidate does not need write permission
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I validate proposed method body "{ return 42; }"
    Then method syntax diagnostics are valid
    And the source file contains "return 1;"

  Scenario: An incomplete block returns a structured validation error
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I validate proposed method body "{ return 42;"
    Then method syntax diagnostics are invalid
    And method syntax diagnostics include "CS4L0001"
    And the source file contains "return 1;"

  Scenario: Check a changed method on disk without requiring a new index
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I replace the selected method body with "{ return 42; }"
    And I fetch the current method syntax diagnostics
    Then method syntax diagnostics are valid
    And the diagnostics refer to the updated source hash
    And the source file contains "return 42;"

  Scenario: Detect syntax errors introduced by an external editor after indexing
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I replace indexing source "Worker.cs" with "class Worker { public int Run() { return 42 } }"
    And I fetch the current method syntax diagnostics
    Then method syntax diagnostics are invalid
    And method syntax diagnostics include "CS1002"
    And the diagnostics refer to the updated source hash

  Scenario: Do not report unrelated errors elsewhere in the file
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          public int Run() { return 1; }
          public int Other() { return 42 }
      }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I fetch the current method syntax diagnostics
    Then method syntax diagnostics are valid

  Scenario: Syntax checking does not claim to validate missing symbols or types
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return Unknown.Symbol; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I fetch the current method syntax diagnostics
    Then method syntax diagnostics are valid

  Scenario: A method selection must still match its expected name
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I attempt to fetch method diagnostics named "Different"
    Then the syntax diagnostics request fails with "No C# method exists"

  Scenario: Diagnostic results can be read in bounded pages
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          public void Run()
          {
              var first = ;
              var second = ;
              var third = ;
          }
      }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read current method diagnostics with page size 1
    Then method syntax diagnostics are invalid
    And method syntax diagnostics span multiple pages
    And every diagnostic belongs to "Worker.cs"

  Scenario: Validation refuses a stale indexed file without writing
    Given indexing source "Worker.cs" is:
      """
      class Worker { public int Run() { return 1; } }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I read the selected method body
    And I replace indexing source "Worker.cs" with "class Worker { public int Run() { return 10; } }"
    And I attempt to validate proposed method body "{ return 42; }"
    Then the syntax diagnostics request fails with "source file has changed since indexing"
    And the source file contains "return 10;"
