Feature: Persist a repository index before starting MCP
  As a developer preparing a large checkout
  I want to index every selected source file with observable progress
  So that MCP can query an incrementally published snapshot without repeating analysis

  Background:
    Given a Git checkout configured for persistent indexing

  Scenario: Index every file beyond the preview limit
    Given the indexing checkout contains 250 C# files
    When I index the checkout from the console
    Then the index command succeeds with 250 indexed files
    And a new store reads a published snapshot with 250 files
    And MCP finds declarations in the saved snapshot
    And the cache contains derived metadata without source text

  Scenario: Full indexing reports the cross-file reference pass after file indexing
    Given the indexing checkout contains these sources:
      | path      | source                                                   |
      | Worker.cs | class Worker { public void Run() {} }                      |
      | Caller.cs | class Caller { void Go() { new Worker().Run(); } }        |
    When I index the checkout
    Then progress reports reference analysis and persistence before verification
    And indexing progress reaches completion after publication

  Scenario: Redirected output identifies reference analysis and SQLite publication
    Given the indexing checkout contains these sources:
      | path      | source                                                   |
      | Worker.cs | class Worker { public void Run() {} }                      |
      | Caller.cs | class Caller { void Go() { new Worker().Run(); } }        |
    When I index the checkout from the console
    Then redirected output identifies reference analysis and SQLite publication

  Scenario: Reusing unchanged references skips the semantic pass without going silent
    Given the indexing checkout contains 2 C# files
    When I index the checkout
    And I index the checkout again
    Then reference reuse is visible without repeating semantic resolution

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

  Scenario: Resume after repeated interruptions and reanalyze changed files
    Given the indexing checkout contains 4 C# files
    When I stop indexing after 3 files
    And I stop indexing after 1 file
    And I replace indexing source "src/File0001.cs" with "public class EditedType {}"
    And I index the checkout again
    Then the run analyzes 2 files and reuses 2 files
    And declaration "EditedType" is stored at "src/File0001.cs"
    And only one snapshot remains for the indexed branch

  Scenario: Force analysis even when published and partial file hashes match
    Given the indexing checkout contains 3 C# files
    And the checkout has a completed index
    When I stop indexing after 2 files
    And I force indexing from the console
    Then the index command succeeds with 3 indexed files
    And the console reports 3 analyzed files and 0 reused files

  Scenario: Start with one folder and reuse it when indexing the checkout
    Given the indexing checkout contains these sources:
      | path                          | source                    |
      | src/XXX.Domain/sub/Order.cs   | public class Order {}     |
      | src/XXX.Domain/bin/Ignored.cs | public class Ignored {}   |
      | src/XXX.Domain.Api/Api.cs     | public class Api {}       |
    When I index only folder "./src/XXX.Domain/" from the console
    Then the folder command succeeds with 1 matching files
    And the dashboard shows declaration "Order" for branch "main"
    When I read the saved index status
    Then the status has a published partial snapshot with 1 files
    When I index the checkout again
    Then the run analyzes 1 files and reuses 1 files
    And declaration "Order" is stored at "src/XXX.Domain/sub/Order.cs"

  Scenario: A folder run preserves the published checkout and skips excluded ancestors
    Given the indexing checkout contains these sources:
      | path                    | source                      |
      | Core/Model.cs           | public class BeforeType {}  |
      | vendor/Lib/Generated.cs | public class Generated {}   |
    And the checkout has a completed index
    When I replace indexing source "Core/Model.cs" with "public class AfterType {}"
    And I index only folder "Core" from the console
    Then the folder command succeeds with 1 matching files
    And the saved index does not contain "BeforeType"
    And declaration "AfterType" is stored at "Core/Model.cs"
    And the dashboard shows declaration "AfterType" for branch "main"
    When I index only folder "vendor/Lib" from the console
    Then the folder command succeeds with 0 matching files
    And the dashboard shows declaration "AfterType" for branch "main"

  Scenario Outline: Invalid folder requests do not start an index update
    Given the indexing checkout contains 1 C# file
    When I index only folder "<folder>" from the console
    Then the folder command fails
    And no index database was created

    Examples:
      | folder          |
      | ../outside      |
      | missing         |
      | src/File0000.cs |

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
    And the dashboard shows declaration "<visible>" for branch "main"

    Examples:
      | failure              | visible |
      | cancellation         | OldType |
      | analyzer failure     | OldType |
      | source modification  | NewType |
      | branch switch        | NewType |
      | configuration change | NewType |
      | source deletion      | NewType |

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

  Scenario: Changing the project display name preserves reusable declarations
    Given the indexing checkout contains 2 C# files
    And the checkout has a completed index
    When the indexing project name changes to "Renamed Project"
    And I index the checkout again
    Then the run analyzes 0 files and reuses 2 files
    And only one snapshot remains for the indexed branch

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



  Scenario: Blazor components and classic Razor views are persisted and invalidated like C# sources
    Given the indexing checkout contains these sources:
      | path                        | source                                                                 |
      | Components/Counter.razor    | <h1>Counter</h1> @code { private void Increment() {} }                   |
      | Views/Home/Index.cshtml     | <h1>Home</h1> @functions { public string Greeting() => "Welcome"; }     |
    When I index the checkout
    Then a new store reads a published snapshot with 2 files
    And declaration "Counter" is stored at "Components/Counter.razor"
    And declaration "Increment" is stored at "Components/Counter.razor"
    And declaration "Index" is stored at "Views/Home/Index.cshtml"
    And declaration "Greeting" is stored at "Views/Home/Index.cshtml"
    When I index the checkout again
    Then a new store reads a published snapshot with 2 files
    And the run reuses both Razor files
    When I delete indexing source "Views/Home/Index.cshtml"
    And I index the checkout again
    Then a new store reads a published snapshot with 1 files
    And the saved index does not contain "Greeting"
    And declaration "Counter" is stored at "Components/Counter.razor"

  Scenario: Indexing successive folders accumulates MCP-searchable declarations
    Given the indexing checkout contains these sources:
      | path                  | source                                   |
      | src/Navigation/A.cs   | public class NavigationIndex {}          |
      | src/Search/B.cs       | public class SemanticSearchEngine {}     |
      | src/Other/C.cs        | public class NotIndexedYet {}            |
    When I incrementally index folder "src/Navigation"
    Then the cumulative partial index contains 1 files
    And the partial snapshot contains declaration "NavigationIndex"
    And the partial snapshot does not contain declaration "SemanticSearchEngine"
    When I incrementally index folder "src/Search"
    Then the cumulative partial index contains 2 files
    And the partial snapshot contains declaration "NavigationIndex"
    And the partial snapshot contains declaration "SemanticSearchEngine"
    And the partial snapshot does not contain declaration "NotIndexedYet"
    And the last folder index analyzed only 1 file
    And FTS5 search of the partial snapshot finds "SemanticSearchEngine"

  Scenario: Reindexing one folder replaces only that folder and retains other folders
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class OldNavigationType {}     |
      | src/Search/B.cs       | public class SemanticSearchEngine {}  |
    When I incrementally index folder "src/Navigation"
    And I incrementally index folder "src/Search"
    And I replace indexing source "src/Navigation/A.cs" with "public class NewNavigationType {}"
    And I incrementally index folder "src/Navigation"
    Then the cumulative partial index contains 2 files
    And the partial snapshot contains declaration "NewNavigationType"
    And the partial snapshot contains declaration "SemanticSearchEngine"
    And the partial snapshot does not contain declaration "OldNavigationType"
    And the last folder index analyzed only 1 file

  Scenario: A deleted file disappears only when its folder is reindexed
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
      | src/Navigation/B.cs   | public class DeletedNavigation {}     |
      | src/Search/C.cs       | public class SemanticSearchEngine {}  |
    When I incrementally index folder "src/Navigation"
    And I incrementally index folder "src/Search"
    And I delete indexing source "src/Navigation/B.cs"
    And I incrementally index folder "src/Navigation"
    Then the cumulative partial index contains 2 files
    And the partial snapshot does not contain declaration "DeletedNavigation"
    And the partial snapshot contains declaration "NavigationIndex"
    And the partial snapshot contains declaration "SemanticSearchEngine"

  Scenario: An interrupted earlier index can supply cached declarations to a completed folder
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
      | src/Search/B.cs       | public class SemanticSearchEngine {}  |
    When I stop indexing after 1 files
    And I incrementally index folder "src/Search"
    Then the cumulative partial index contains 2 files
    And the partial snapshot contains declaration "NavigationIndex"
    And the partial snapshot contains declaration "SemanticSearchEngine"

  Scenario: Folder indexes remain isolated by named Git branch
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
      | src/Search/B.cs       | public class SemanticSearchEngine {}  |
    When I incrementally index folder "src/Navigation"
    And I switch the indexing checkout to a new branch "feature/search"
    And I incrementally index folder "src/Search"
    Then the cumulative partial index contains 1 files
    And the partial snapshot contains declaration "SemanticSearchEngine"
    And the partial snapshot does not contain declaration "NavigationIndex"
    And branch "main" retains declaration "NavigationIndex"

  Scenario: Partial indexing refuses to discard an existing incompatible snapshot
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
      | src/Search/B.cs       | public class SemanticSearchEngine {}  |
    When I incrementally index folder "src/Navigation"
    And the indexing description language changes to "fr"
    And I attempt to incrementally index folder "src/Search"
    Then partial indexing is rejected without replacing the published snapshot
    And the partial snapshot contains declaration "NavigationIndex"
    And the partial snapshot does not contain declaration "SemanticSearchEngine"

  Scenario: Partial snapshots cannot claim complete cross-folder Roslyn references
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
    When I incrementally index folder "src/Navigation"
    Then partial reference lookup is reported as unavailable

  Scenario: Completing the checkout replaces the partial scope with a full index
    Given the indexing checkout contains these sources:
      | path                  | source                                |
      | src/Navigation/A.cs   | public class NavigationIndex {}       |
      | src/Search/B.cs       | public class SemanticSearchEngine {}  |
    When I incrementally index folder "src/Navigation"
    And I index the checkout again
    Then the resulting index is complete with 2 files
    And the run analyzes 1 files and reuses 1 files
