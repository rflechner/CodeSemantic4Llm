Feature: Locate exact usages of indexed C# methods and types
  As a coding agent investigating an implementation
  I want bounded references to one selected declaration
  So that unrelated names and overloads do not consume my context

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Different overloads are resolved independently across files
    Given the indexing checkout contains these sources:
      | path      | source                                                                                                    |
      | Worker.cs | namespace Demo; public class Worker { public void Run() {} public void Run(int value) {} }               |
      | Caller.cs | namespace Demo; class Caller { void Use() { var worker = new Worker(); worker.Run(); worker.Run(1); } }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 1 references without another page
    And all usages point to "Caller.cs"
    When I select indexed "Run" declaration number 2
    And I find its usages with page size 50
    Then the usage listing contains 1 references without another page
    And all usages point to "Caller.cs"

  Scenario: Type references include declarations of variable types and object construction
    Given the indexing checkout contains these sources:
      | path      | source                                                                  |
      | Worker.cs | namespace Demo; public class Worker { }                                 |
      | Caller.cs | namespace Demo; class Caller { Worker Value = new Worker(); }           |
    When I index the checkout
    And I select indexed "Worker" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 2 references without another page
    And all usages point to "Caller.cs"

  Scenario: A same-named method in another type is not a reference
    Given the indexing checkout contains these sources:
      | path      | source                                                                |
      | Worker.cs | class Worker { public void Run() {} }                                 |
      | Other.cs  | class Other { public void Run() {} }                                   |
      | Caller.cs | class Caller { void Use() { new Worker().Run(); new Other().Run(); new Other().Run(); } }  |
    When I index the checkout
    And I select indexed "Run" declaration number 2
    And I find its usages with page size 50
    Then the usage listing contains 1 references without another page
    And all usages point to "Caller.cs"

  Scenario: Partial type declarations share resolved references without conflating unrelated types
    Given the indexing checkout contains these sources:
      | path      | source                                                    |
      | First.cs  | partial class Worker { public void First() {} }           |
      | Second.cs | partial class Worker { public void Second() {} }          |
      | Caller.cs | class Caller { Worker Value = new Worker(); }             |
    When I index the checkout
    And I select indexed "Worker" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 2 references without another page
    When I select indexed "Worker" declaration number 2
    And I find its usages with page size 50
    Then the usage listing contains 2 references without another page

  Scenario: Reference pages can traverse more than 200 call sites
    Given a method with 250 call sites in the indexing checkout
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I find its usages with page size 200
    Then more usages are available
    When I read all remaining usage pages
    Then the usage listing contains 250 references without another page
    And all usages point to "Caller.cs"

  Scenario: Reusing unchanged files also preserves resolved reference rows
    Given the indexing checkout contains these sources:
      | path      | source                                                       |
      | Worker.cs | class Worker { public void Run() {} }                        |
      | Caller.cs | class Caller { void Use() { new Worker().Run(); } }           |
    When I index the checkout
    And I index the checkout again
    Then the run analyzes 0 files and reuses 2 files
    When I select indexed "Run" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 1 references without another page

  Scenario: References are recalculated when an unchanged caller loses its target
    Given the indexing checkout contains these sources:
      | path      | source                                                       |
      | Worker.cs | class Worker { public void Run() {} }                        |
      | Caller.cs | class Caller { void Use() { new Worker().Run(); } }           |
    When I index the checkout
    And I replace indexing source "Worker.cs" with "class Worker { public void Run(int value) {} }"
    And I index the checkout again
    When I select indexed "Run" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 0 references without another page

  Scenario: Deleted callers do not survive a new snapshot
    Given the indexing checkout contains these sources:
      | path      | source                                                       |
      | Worker.cs | class Worker { public void Run() {} }                        |
      | Caller.cs | class Caller { void Use() { new Worker().Run(); } }           |
    When I index the checkout
    And I delete indexing source "Caller.cs"
    And I index the checkout again
    When I select indexed "Run" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 0 references without another page

  Scenario Outline: Invalid usage requests fail explicitly
    Given the indexing checkout contains these sources:
      | path      | source                                  |
      | Worker.cs | class Worker { public void Run() {} }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I request usages with "<problem>"
    Then the usage request fails with "<error>"

    Examples:
      | problem          | error                     |
      | excessive limit  | Limit must not exceed 200 |
      | negative offset  | offset                    |
      | parent traversal | repository-relative path  |
      | missing location | No indexed method or type |

  Scenario: Snapshot replacement invalidates a previous selection
    Given the indexing checkout contains these sources:
      | path      | source                                  |
      | Worker.cs | class Worker { public void Run() {} }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I index the checkout again
    And I attempt to find its usages
    Then the usage request fails with "snapshot changed"

  Scenario: Changed HEAD invalidates usage queries
    Given the indexing checkout contains these sources:
      | path      | source                                  |
      | Worker.cs | class Worker { public void Run() {} }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I commit the indexing checkout
    And I attempt to find its usages
    Then the usage request fails with "HEAD has changed"

  Scenario: References are not read from another branch
    Given the indexing checkout contains these sources:
      | path      | source                                  |
      | Worker.cs | class Worker { public void Run() {} }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    And I switch the indexing checkout to a new branch "feature/references"
    And I attempt to find its usages
    Then the usage request fails with "No published index"

  Scenario: A version-two cache requires reindexing before usage queries
    Given the indexing checkout contains these sources:
      | path      | source                                  |
      | Worker.cs | class Worker { public void Run() {} }   |
    When I index the checkout
    And I select indexed "Run" declaration number 1
    Given the cache uses the previous version-two schema
    When I attempt to find its usages
    Then the usage request fails with "Run the index command again"
    And the cache schema version is 2
    When I index the checkout again
    Then the cache schema version is 6
    When I select indexed "Run" declaration number 1
    And I find its usages with page size 50
    Then the usage listing contains 0 references without another page
