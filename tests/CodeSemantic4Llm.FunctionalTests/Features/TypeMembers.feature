Feature: Navigate from an indexed type to its declared members
  As an agent exploring a class
  I want to list its methods without guessing their names
  So that I can inspect the relevant declarations with bounded MCP calls

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: List direct methods including overloads and explicit interface implementations
    Given indexing source "Worker.cs" is:
      """
      namespace Demo;
      interface IWorker { void Work(); }
      class Parent { public void Inherited() {} }
      class Worker : Parent, IWorker
      {
          public Worker() {}
          public int Count { get; set; }
          void IWorker.Work() {}
          public void Run() {}
          public void Run(int count) {}
          public class Nested { public void Inner() {} }
      }
      """
    When I index the checkout
    And I find type "Worker" in "Worker.cs"
    And I list its "method" members with page size 2
    Then more type members are available
    When I read the remaining type member pages
    Then the listed member names are:
      | name |
      | Work |
      | Run  |
      | Run  |
    And every listed member belongs to the selected type declaration

  Scenario: Reused index rows retain type membership
    Given indexing source "Worker.cs" is:
      """
      class Worker
      {
          public Worker() {}
          public int Value;
          public int Count { get; set; }
          public void Run() {}
          class Nested { public void Inner() {} }
      }
      """
    When I index the checkout
    And I index the checkout again
    Then the run analyzes 0 files and reuses 1 files
    When I find type "Worker" in "Worker.cs"
    And I list its "all" members with page size 50
    Then the listed member names are:
      | name   |
      | Worker |
      | Value  |
      | Count  |
      | Run    |
      | Nested |
    And every listed member belongs to the selected type declaration

  Scenario: Partial and homonymous declarations in different files are not silently merged
    Given the indexing checkout contains these sources:
      | path     | source                                                          |
      | First.cs | namespace Demo; partial class Worker { public void First() {} }  |
      | Other.cs | namespace Demo; partial class Worker { public void Other() {} }  |
    When I index the checkout
    And I find type "Worker" in "First.cs"
    And I list its "method" members with page size 50
    Then the listed member names are:
      | name  |
      | First |

  Scenario: Empty classes return a successful empty page
    Given the indexing checkout contains these sources:
      | path     | source          |
      | Empty.cs | class Empty {}  |
    When I index the checkout
    And I find type "Empty" in "Empty.cs"
    And I list its "method" members with page size 50
    Then the member listing contains 0 declarations without another page

  Scenario: Pagination can retrieve more than 200 methods
    Given a class with 250 methods in "Large.cs"
    When I index the checkout
    And I find type "Large" in "Large.cs"
    And I list its "method" members with page size 200
    Then more type members are available
    When I read the remaining type member pages
    Then the member listing contains 250 declarations without another page
    And every listed member belongs to the selected type declaration

  Scenario Outline: Invalid member queries fail explicitly
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    When I index the checkout
    And I find type "Worker" in "Worker.cs"
    And I request members with "<problem>"
    Then the member request fails with "<error>"

    Examples:
      | problem          | error                     |
      | unknown kind     | Unknown member kind       |
      | excessive limit  | Limit must not exceed 200 |
      | negative offset  | offset                    |
      | parent traversal | repository-relative path  |
      | method location  | No type declaration       |
      | missing location | No type declaration       |

  Scenario: Pagination rejects an obsolete index generation
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    When I index the checkout
    And I find type "Worker" in "Worker.cs"
    And I index the checkout again
    And I attempt to list its methods
    Then the member request fails with "snapshot changed"

  Scenario: Member queries reject a changed HEAD
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    When I index the checkout
    And I find type "Worker" in "Worker.cs"
    And I commit the indexing checkout
    And I attempt to list its methods
    Then the member request fails with "HEAD has changed"

  Scenario: Member queries do not reuse another branch's snapshot
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    When I index the checkout
    And I find type "Worker" in "Worker.cs"
    And I switch the indexing checkout to a new branch "feature/members"
    And I attempt to list its methods
    Then the member request fails with "No published index"

  Scenario: Reindexing upgrades a version-one cache and reconstructs membership
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    And the checkout has a completed index
    And the cache uses the original version-one schema
    When I index the checkout again
    Then the run analyzes 1 files and reuses 0 files
    And the cache schema version is 8
    When I find type "Worker" in "Worker.cs"
    And I list its "method" members with page size 50
    Then the listed member names are:
      | name |
      | Run  |

  Scenario: An old schema is not silently upgraded by an MCP read
    Given the indexing checkout contains these sources:
      | path      | source                                |
      | Worker.cs | class Worker { public void Run() {} } |
    And the checkout has a completed index
    When I find type "Worker" in "Worker.cs"
    Given the cache uses the original version-one schema
    When I attempt to list its methods
    Then the member request fails with "Run the index command again"
    And the cache schema version is 1

  Scenario: A failed index after schema upgrade preserves the previous declarations
    Given the indexing checkout contains these sources:
      | path     | source                                    |
      | First.cs | class OldType { public void Original() {} } |
    And the checkout has a completed index
    And the cache uses the original version-one schema
    And the next index run will encounter "analyzer failure"
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And the cache schema version is 8
    And the previous snapshot still contains "OldType"
    When I find type "OldType" in "First.cs"
    And I attempt to list its methods
    Then the member request fails with "no type membership metadata"
