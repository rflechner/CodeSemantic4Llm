Feature: Diagnose syntax in current files and types
  As a coding agent repairing existing source
  I want precise syntax errors even when edits invalidate the index
  So that I can find broken code outside the method I was changing

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Find an error introduced after the selected method without reindexing
    Given indexing source "Worker.cs" is:
      """
      class Worker { int Run() => 1; }
      """
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I replace indexing source "Worker.cs" with "class Worker { int Run() => 1; int Later() => 2 }"
    And I fetch the current method syntax diagnostics
    Then method syntax diagnostics are valid
    When I fetch current file diagnostics for "Worker.cs"
    Then current source diagnostics include error "CS1002"
    And current source diagnostics identify the bytes on disk
    And reading source diagnostics leaves the file unchanged

  Scenario: Diagnose a malformed file before any index exists
    Given indexing source "Broken.cs" is:
      """
      class
      """
    When I fetch current file diagnostics for "Broken.cs"
    Then current source diagnostics include error "CS1001"
    And no index database was created
    And reading source diagnostics leaves the file unchanged

  Scenario: Limit diagnostics to the selected class part including nested declarations
    Given indexing source "Worker.cs" is:
      """
      partial class Worker
      {
          class Nested { int Run() => ; }
      }
      partial class Worker { int Other() => ; }
      class Sibling { int Run() => ; }
      """
    When I fetch current type diagnostics for "Worker" in "Worker.cs" at line 1 column 15
    Then current source diagnostics include error "CS1525"
    And current source diagnostics only refer to line 3
    And no index database was created

  Scenario: A moved declaration suggests the file diagnostic fallback
    Given indexing source "Worker.cs" is:
      """

      class Worker { int Run() => ; }
      """
    When I attempt current type diagnostics for "Worker" in "Worker.cs" at line 1 column 7
    Then the current source diagnostic request fails with "get_file_diagnostics"
    When I fetch current file diagnostics for "Worker.cs"
    Then current source diagnostics include error "CS1525"

  Scenario: Read every error across bounded pages
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          int First() => ;
          int Second() => ;
          int Third() => ;
      }
      """
    When I read all current file diagnostics for "Worker.cs" with page size 1
    Then current source diagnostics include error "CS1525"
    And current source diagnostics contain 3 errors across 3 pages

  Scenario: Do not mix diagnostics from different file versions
    Given indexing source "Worker.cs" is:
      """
      class Worker { int First() => ; int Second() => ; }
      """
    When I fetch the first diagnostic page for "Worker.cs"
    And I replace indexing source "Worker.cs" with "class Worker { int First() => 1; }"
    And I attempt the next current source diagnostic page
    Then the current source diagnostic request fails with "Restart at offset 0"

  Scenario: Warnings alone do not make syntax invalid
    Given indexing source "Worker.cs" is:
      """
      #warning Review this
      class Worker { UnknownType Run() => Missing.Value; }
      """
    When I fetch current file diagnostics for "Worker.cs"
    Then current source syntax is valid with warning "CS1030"

  Scenario: Honor configured source extension overrides
    Given indexing source "Worker.csharp" is:
      """
      class Worker { int Run() => ; }
      """
    When I fetch current file diagnostics for "Worker.csharp"
    Then current source diagnostics include error "CS1525"

  Scenario: A page without errors does not hide errors elsewhere in the file
    Given indexing source "Worker.cs" is:
      """
      #warning Review this
      class Worker { int Run() => ; }
      """
    When I fetch the first diagnostic page for "Worker.cs"
    Then the source diagnostic page only contains a warning but the file is invalid

  Scenario Outline: Reject invalid or unpinned pagination
    Given indexing source "Worker.cs" is:
      """
      class Worker { int Run() => ; }
      """
    When I attempt current file diagnostics for "Worker.cs" with limit <limit> and offset <offset> without a hash
    Then the current source diagnostic request fails with "<message>"

    Examples:
      | limit | offset | message             |
      | 0     | 0      | maxDiagnostics      |
      | 201   | 0      | maxDiagnostics      |
      | 50    | -1     | offset              |
      | 50    | 1      | expectedContentHash |

  Scenario: Reject unsupported source languages explicitly
    Given indexing source "Worker.ts" is:
      """
      export class Worker {}
      """
    When I attempt current file diagnostics for "Worker.ts"
    Then the current source diagnostic request fails with "No configured source analyzer"

  Scenario Outline: Reject unsafe paths without leaving the repository boundary
    Given source links in the indexing checkout point outside it
    When I attempt current file diagnostics for "<path>"
    Then the current source diagnostic request fails with "<message>"

    Examples:
      | path                        | message         |
      | ../outside/Outside.cs       | traversal       |
      | ..\outside\Outside.cs       | traversal       |
      | /outside/Outside.cs         | relative        |
      | Linked.cs                   | symbolic link   |
      | linked-directory/Outside.cs | symbolic link   |
