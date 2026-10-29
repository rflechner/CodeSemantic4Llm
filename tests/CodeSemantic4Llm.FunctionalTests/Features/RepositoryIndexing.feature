Feature: Persist a repository index before starting MCP
  As a developer preparing a large checkout
  I want to index every selected source file with observable progress
  So that MCP can query a complete saved snapshot without repeating analysis

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Index every file beyond the preview limit
    Given the indexing checkout contains 250 C# files
    When I index the checkout from the console
    Then the index command succeeds with 250 indexed files
    And a new store reads a published snapshot with 250 files
    And MCP finds declarations in the saved snapshot
    And the cache contains derived metadata without source text

  Scenario: Apply extensions, exclusions and symbolic link boundaries
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | Root.cs               | public class RootType {}              |
      | src/Nested.csharp     | public class NestedType {}            |
      | submodules/Library.cs | public class LibraryType {}           |
      | vendor/Ignored.cs     | public class IgnoredType {}           |
      | src/bin/Generated.cs  | public class GeneratedType {}         |
      | src/obj/Generated.cs  | public class GeneratedType {}         |
      | src/Other.ts          | export class OtherType {}             |
    And source links in the indexing checkout point outside it
    When I index the checkout
    Then a new store reads a published snapshot with 3 files
    And the saved index does not contain "IgnoredType"
    And the saved index does not contain "OutsideType"
    And indexing progress reaches completion after publication

  Scenario: An unchanged second run reuses all analyzed files
    Given the indexing checkout contains 3 C# files
    When I index the checkout
    And I index the checkout again
    Then the run analyzes 0 files and reuses 3 files
    And only one snapshot remains for the indexed branch

  Scenario: Reindex modifications, deletions and renames without stale declarations
    Given the indexing checkout contains these sources:
      | path        | source                     |
      | Change.cs   | public class BeforeType {} |
      | Remove.cs   | public class RemovedType {}|
      | Rename.cs   | public class RenamedType {}|
    When I index the checkout
    And I replace indexing source "Change.cs" with "public class AfterType {}"
    And I delete indexing source "Remove.cs"
    And I rename indexing source "Rename.cs" to "Moved.cs"
    And I index the checkout again
    Then a new store reads a published snapshot with 2 files
    And the saved index does not contain "BeforeType"
    And the saved index does not contain "RemovedType"
    And declaration "RenamedType" is stored at "Moved.cs"

  Scenario: Branches with slash-separated names keep independent snapshots
    Given the indexing checkout contains these sources:
      | path      | source                   |
      | Shared.cs | public class MainType {} |
    When I index the checkout
    And I switch the indexing checkout to a new branch "feature/indexing"
    And I replace indexing source "Shared.cs" with "public class FeatureType {}"
    And I index the checkout again
    Then branch "main" retains declaration "MainType"
    And branch "feature/indexing" retains declaration "FeatureType"
    And the saved index does not contain "MainType"

  Scenario: Separate repositories do not share declaration records
    Given the indexing checkout contains 1 C# file
    When I index the checkout
    And I index a different checkout containing "OtherRepositoryType"
    Then a new store reads a published snapshot with 1 files
    And the saved index does not contain "OtherRepositoryType"
    And the cache has separate databases for the two checkouts

  Scenario Outline: An interrupted run preserves the previous snapshot
    Given the indexing checkout contains these sources:
      | path     | source                  |
      | First.cs | public class OldType {} |
    And the checkout has a completed index
    And I replace indexing source "First.cs" with "public class NewType {}"
    And the next index run will encounter "<failure>"
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And the previous snapshot still contains "OldType"
    And only one snapshot remains for branch "main"

    Examples:
      | failure                  |
      | cancellation             |
      | analyzer failure         |
      | source modification      |
      | branch switch            |
      | configuration change     |
      | source deletion          |

  Scenario: Readers see the old snapshot until the replacement commits
    Given the indexing checkout contains these sources:
      | path     | source                  |
      | First.cs | public class OldType {} |
    And the checkout has a completed index
    And I replace indexing source "First.cs" with "public class NewType {}"
    And a reader queries the index during the next analysis
    When I index the checkout
    Then the reader observed the previous complete snapshot
    And the saved index does not contain "OldType"
    And declaration "NewType" is stored at "First.cs"

  Scenario: Configuration and analyzer changes invalidate reused declarations
    Given the indexing checkout contains 2 C# files
    When I index the checkout
    And the analyzer indexing version changes
    And I index the checkout again
    Then the run analyzes 2 files and reuses 0 files
    When the indexing description language changes to "fr"
    And I index the checkout again
    Then the run analyzes 2 files and reuses 0 files
    And the published semantic language is "fr"

  Scenario: An empty repository publishes a valid empty index
    When I index the checkout from the console
    Then the index command succeeds with 0 indexed files
    And a new store reads a published snapshot with 0 files

  Scenario Outline: Invalid configuration fails before cache creation
    Given persistent indexing configuration is "<problem>"
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And no index database was created

    Examples:
      | problem              |
      | missing              |
      | unsupported analyzer |
      | invalid language     |

  Scenario: Symbol queries are bounded and deterministic
    Given the indexing checkout contains 3 C# files
    When I index the checkout
    Then querying "SharedType" with a limit of 2 returns 2 ordered results and indicates more
    And a symbol query with a limit of 201 is rejected

  Scenario: A changed HEAD is not presented as the indexed revision
    Given the indexing checkout contains 1 C# file
    When I index the checkout
    And I commit the indexing checkout
    Then MCP rejects searches until the checkout is reindexed

  Scenario: Index help does not require a repository or create storage
    When I request index command help
    Then index help succeeds
    And no index database was created

  Scenario: An incompatible schema is left untouched
    Given the indexing checkout contains 1 C# file
    And the checkout has a completed index
    And the index schema version is newer than supported
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And the newer schema version remains unchanged

  Scenario Outline: Reject a path without a named Git checkout root
    Given the indexing target is "<target>"
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And no index database was created

    Examples:
      | target        |
      | not Git       |
      | subdirectory  |
      | detached HEAD |

  Scenario: Index a branch before its first commit
    Given the indexing checkout is on an unborn branch
    And the indexing checkout contains 1 C# file
    When I index the checkout
    Then a new store reads a published snapshot with 1 files
    And the snapshot has no recorded revision

  Scenario: Reading missing index status does not initialize storage
    When I read the saved index status
    Then the status has no snapshot
    And no index database was created

  Scenario: New exclusions remove previously indexed declarations
    Given the indexing checkout contains these sources:
      | path         | source                    |
      | Included.cs  | public class Included {}  |
      | Excluded.cs  | public class Excluded {}  |
    And the checkout has a completed index
    When indexing excludes "**/Excluded.cs"
    And I index the checkout again
    Then a new store reads a published snapshot with 1 files
    And the saved index does not contain "Excluded"

  Scenario: Exact names preserve case and Unicode distinctions
    Given the indexing checkout contains these sources:
      | path      | source                                        |
      | Names.cs  | public class École {} public class école {}    |
    When I index the checkout
    Then declaration "École" is stored at "Names.cs"
    And declaration "école" is stored at "Names.cs"
    And the saved index does not contain "Ecole"

  Scenario: Reject cache storage inside the source checkout
    Given the index cache is inside the checkout
    When I attempt to index the checkout
    Then indexing fails without publishing a snapshot
    And no index database was created
