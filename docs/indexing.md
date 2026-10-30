# Indexing a checkout before MCP startup

From the CodeSemantic4Llm checkout:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/repository
```

Relative paths are resolved from the process working directory. Quote paths containing
spaces. In Rider, use `index /path/to/repository` as the program arguments. `index --help`
prints usage without accessing the checkout or cache.

The target must be the root of a Git working tree on a named branch and contain
`codesemantic4llm.yaml`. An unborn branch (before its first commit) is supported with a
null recorded revision. Detached HEAD and paths inside a checkout are rejected. Use
`init <repository-path>` first when the YAML is absent.

## Scope and progress

Indexing processes **all** source files selected by the YAML, without the preview's
200-path cap. It uses the same analyzer priority, extension overrides and exclusions as
preview. Built-in exclusions always apply; submodules are included unless excluded.
Symlinked files and directories are skipped, and each source path is checked before and
after reading. The command does not run target builds, restore packages or execute source.

Currently `dotnet-csharp` extracts declarations using Roslyn syntax trees, including
language, kind, names, locations and immediate containing types. During indexing,
an ephemeral `CSharpCompilation` and `SemanticModel` resolve references to indexed
methods and types across the selected C# files. Only target/usage locations are stored:
no source text, copied project files or Roslyn syntax trees are persisted. The reference
pass loads the selected C# files into memory, so its peak memory scales with the size of
the compilation. References to framework and other external declarations are not saved.
Optional OpenAI-compatible LLM summaries of methods and types are available through
`semanticIndexing.llm` in each repository YAML. The feature is disabled by default.
When enabled, the C# extractor supplies full declarations (including XML doc comments)
to the model; only concise summaries (maximum 10 lines) are stored beside symbols.
Embeddings remain planned; see [configuration](repository-configuration.md#optional-per-repository-llm-summaries).

**Current limitation:** The reference pass treats all selected C# files as one assembly;
it does not evaluate `.csproj` boundaries, target frameworks, conditional compilation
symbols, custom assembly references or NuGet/project graphs. It uses runtime framework
references only. Roslyn can resolve simple cross-file declarations, overloads, partial
types and reduced extension methods, but incomplete/ambiguous bindings are omitted rather
than guessed. Thus a zero-result query does not prove there are no callers in a complex
multi-project repository.

An interactive terminal displays Spectre progress for discovery, indexing, verification
and publication. Discovery has an unknown total; indexing and verification show file
counts and percentages. The final summary includes the absolute SQLite database path,
branch, recorded HEAD, file/declaration counts, analyzed/reused counts and snapshot ID.
Redirected output contains this summary as plain text. An IDE
console that does not advertise interactivity also receives the plain summary.

Exit status is 0 on success/help, 1 for invalid input or operation failure, and 130 for
cancellation. Ctrl+C cancels the run. Completion is reported only after publication.

## Cache and isolation

By default the cache is located under the platform's .NET `LocalApplicationData` folder:

```text
CodeSemantic4Llm/indexes/<sha256-of-canonical-checkout-root>/index.sqlite
```

Set `CODESEMANTIC4LLM_INDEX_ROOT` to an absolute directory outside the source checkout to
override it. Use the same cache setting for `index` and the MCP server, for example:

```sh
export CODESEMANTIC4LLM_INDEX_ROOT=/absolute/path/to/index-cache
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/repository
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- serve /path/to/repository
```

Each checkout has its own database, including separate Git worktrees. Each named branch
has one completed snapshot; switching branches never merges records. Branch names are
stored in SQLite, not interpolated into paths. Renaming or moving a checkout selects a
different cache identity. No YAML or source files are copied into the cache.

The versioned schema stores derived declarations, reference locations and
lookup/invalidation metadata. It is a regenerable cache, not a stable interchange format
or a source backup. Schema 2 introduced type membership, schema 3 adds a compact
reference table indexed by target declaration position, and schema 4 adds optional LLM
summaries to each symbol. Running `index` upgrades old caches on the write path; the old published snapshot remains intact until
atomic publication. An earlier branch may still return its declarations, but cannot
answer `find_usages` until reindexed. An interrupted run preserves the older published
snapshot. MCP reads never migrate storage.

Unsupported schema versions fail without changes; select a fresh cache directory and rerun
`index`. Deleting an unused cache directory is sufficient to discard its derived data.

## Reindexing and publication

Rerun the same command after edits. Content hashes, analyzer identity/version and a
configuration fingerprint decide whether prior declarations can be reused. New and
changed files are analyzed; deleted files and old rename locations disappear on publication.
Changes to analyzer selection, extensions, exclusions, semantic language or LLM settings
invalidate reuse. Unchanged files reuse their previously generated summaries without LLM calls.
All selected files are read and hashed even when their declarations can be reused.
References are global semantic relationships: when any selected file is added, deleted
or changed, all C# references are resolved again against the new compilation. Only an
identical, fully reused set of files may copy all reference rows from the previous
snapshot. This prevents an unchanged caller from retaining a stale edge after its
target is edited.

One SQLite transaction stages the replacement. Publication switches the branch snapshot
and removes its old generation atomically. Other branches retain their snapshots. A failed
or canceled run, including an LLM failure, rolls back; readers continue to see the last
completed snapshot. A first
failed run may leave an initialized database with no published snapshot. Concurrent writers
are serialized by SQLite and fail if the lock timeout is exceeded.

Before commit, the command rehashes source files, checks the discovered paths, and rereads
Git state and YAML settings. Detected concurrent changes abort publication. This is a
best-effort check rather than an atomic filesystem snapshot: avoid editing or switching
branches during indexing. The recorded HEAD identifies observed Git history; it does not
prove that working-tree content matches that commit or remains unchanged afterward.

## Querying through MCP

MCP startup never indexes automatically. It selects the checkout from `serve [repository-path]`,
falling back to `CODESEMANTIC4LLM_REPOSITORY_ROOT` when the argument is omitted, and uses the
same cache path as the CLI. The argument accepts an existing absolute or relative directory;
relative paths are resolved against the process working directory. See the
[MCP client configuration example](../README.md#mcp-client-configuration).

### Default code-focused interface

The default `serve` command and argument-free startup hide indexing concepts from
models. They do not advertise the configuration, preview or index-status tools.
Descriptions, argument schemas and responses omit snapshot IDs, index metadata,
SQLite details, file hashes and reindexing instructions.

- `find_symbols` returns `symbols` and `hasMore`. Declarations contain their name,
  qualified name, kind and source location, with no temporary identity token.
- `get_type_members`, `find_usages`, `get_method_body`, `validate_method_body` and
  `replace_method_body` select code with `relativePath` and `name` (simple or qualified).
  Optional `line` and `column` identify the declaration when names are ambiguous.
  Ambiguous requests fail rather than selecting an arbitrary overload.
  Qualified names can omit leading namespace or enclosing-type segments:
  `CliApplication.RunAsync` can select `TinyHarness.Cli.CliApplication.RunAsync`.
  Matching uses an exact, case-sensitive suffix starting at a dot boundary within
  the requested file; it never drops an incorrect qualifier or treats `%`/`_` as
  wildcards. All matching declarations count toward ambiguity, including overloads
  and types in different namespaces. Source positions narrow this same candidate set.
- Member queries additionally accept `kind`; navigation takes `maxResults` and `offset`.
  Responses contain the selected declaration, members/usages and pagination only.
  Copy `nextOffset` unchanged into the next request; continuing after a source generation
  changes is rejected instead of silently mixing results.
- `get_method_body` reads current source without an index or preceding search. It accepts
  `maxCharacters` and `offset` and returns `method`, `body` and character pagination.
  Copy `nextOffset` unchanged when continuing; the source version is retained internally
  for consistent pagination and later writes.
- `validate_method_body` accepts `newBody` with or without outer braces and diagnostic pagination. It checks current
  source without requiring a prior read. `replace_method_body` accepts `newBody` after
  a `get_method_body` call and checks the file version recorded during that read.
  Its response contains only `methodLocation`. No default tool accepts or returns hashes.
- File/type/method diagnostic tools keep their current-file path/position arguments.
  Start diagnostics at `offset: 0`, then copy `nextOffset` unchanged into the next request.
  Each continuation retains its query and file version, including the proposed body
  identity for validation. Changing the code or scope requires starting again.
  Navigation, method bodies and diagnostics each retain at most 4,096 continuations,
  with no source text.

Method addresses do not expire and work across server restarts. Read observations are
server-local metadata, keyed by checkout and declaration location, separately from search
results. A write checks the observed branch, revision, declaration and actual file bytes.
After a restart or eviction of one of the 4,096 read observations, read the method again
before writing. Reindexing unchanged source does not invalidate the read, and sequential
edits with a fresh read need no index refresh between writes.

This presentation does not add automatic indexing. Navigation still uses derived data;
missing/stale information produces a short error requesting operator assistance.
Direct method operations and syntax diagnostics work independently of that data.
Technical failures are logged to stderr and optionally through [`serve --log-file`](logging.md).
Continue running the operator's CLI `index` command after source/configuration changes
when fresh navigation results are needed.

### Detailed interface with explicit indexing exposure

Start `serve /path/to/repository --expose-indexing` to expose the following original
interface and the configuration/preview tools. This is independent of `--allow-write`;
neither a tool argument nor repository YAML can enable either capability. In this mode,
explicit declaration location and `snapshotId` are required for navigation and editing, and
responses include their original snapshot metadata and `reindexRequired` write flag.
Only this detailed interface exposes `contentHash` and `expectedContentHash`; its
diagnostic offsets remain numeric row positions with explicit version checks.

- `get_repository_index_status` returns observed Git state and the last saved snapshot
  for that branch, or `null` when no snapshot exists. Reading status does not create a cache.
- `find_symbols` accepts `name` and optional `maxResults` (default 20, range 1–200).
  It matches the exact, case-sensitive simple name and returns declaration metadata,
  snapshot identity and `hasMore`. Ordering is deterministic by path, line, column and
  declaration order. It does not return source text or perform substring/semantic search.
- `get_type_members` lists direct members of one type declaration by its relative path,
  line, column and snapshot ID from `find_symbols`. The optional `kind` filter selects
  `method`, `constructor`, `property`, `field`, `event`, or another supported declaration
  kind; omission returns all indexed direct members. Pages default to 50 results, with
  a maximum of 200. Start with `offset: 0` and follow `nextOffset` until it is null.
  `hasMore` reports whether a next page exists. A changed snapshot requires a new lookup,
  preventing pagination across different index generations.
- `find_usages` accepts an indexed **method or type** declaration's `relativePath`,
  `line`, `column` and `snapshotId` returned by `find_symbols`. It retrieves Roslyn-resolved
  occurrences, not text matches, from the published SQLite reference index. Pages default
  to 50 (maximum 200); follow `nextOffset` and check `hasMore`. Partial class declarations
  share type references; overloads remain separate. Other declarations (such as fields and
  properties) are not yet supported. Missing reference metadata requires reindexing.
- `get_method_body` reads only one indexed C# method body using its exact declaration
  location and snapshot ID. It verifies that the current file's bytes match the published
  source hash. By default it returns at most 12,000 characters; follow `nextOffset` to
  read more (maximum 20,000 per request). Results include `contentHash` and whether the
  method is expression-bodied.
- `replace_method_body` changes only that method's executable body on disk. It is
  registered only when the operator uses `serve --allow-write`. It requires a matching
  `contentHash` from `get_method_body` and a C# body with or without outer braces. No tool call or
  repository YAML setting can enable write access once the server starts.

A missing branch snapshot or a different HEAD produces an error asking the operator to
run `index`. Queries do not rehash the working tree or reload YAML to establish freshness;
rerun `index` after source/configuration changes, even if HEAD has not changed. Locations
refer to the recorded snapshot and can become stale after edits. Existing preview tools
remain bounded and read-only.

## Listing a class's methods

For a class called `Worker`, ask the model to find `Worker` and list its methods using
CodeSemantic4Llm. The MCP calls are equivalent to:

```json
{"name": "Worker", "maxResults": 20}
```

Then call `get_type_members` with the selected class's path and name:

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker",
  "kind": "method",
  "maxResults": 50,
  "offset": 0
}
```

The result contains the selected type, its matching members and pagination information. Overloads remain distinct by source location. Methods of nested
classes and inherited methods are excluded from the containing class's method list.
Constructors use `kind: "constructor"`; compiler-generated members, operators and property
accessors are not extracted as ordinary methods. Source bodies are never returned or stored.

Selection uses the declaration's exact location, so homonymous or generic types are not
merged by their textual names. Partial types are handled declaration by declaration: query
each intended part returned by `find_symbols`. Resolving all parts within a compilation,
inheritance and full method signatures requires future semantic analysis.


## Finding usages of a method or type

First call `find_symbols` with `{"name":"Run"}`. Select a method declaration from its
`symbols` array, using its qualified name, containing type and location to distinguish
homonyms and overloads. Then call `find_usages` (add `line` and `column` if needed):

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker.Run",
  "maxResults": 50,
  "offset": 0
}
```

The result contains `target`, `usages` (each a relative path, line
and column), `hasMore` and `nextOffset`. For a class, usages include explicitly
written type names such as `new Worker()`, type annotations, base classes and
type arguments when Roslyn resolves them. For a method, overloads and extension
method calls are matched to the declared method rather than its simple name.
An unresolvable use, dynamic call or reflective invocation is not inferred.
No source bodies are read during `find_usages`; indexing must be rerun after edits.


## Checking current source syntax

Four read-only MCP tools use Roslyn's C# parser without starting MSBuild, running
application code or writing any files:

- `get_file_diagnostics` takes only `relativePath` and checks the **whole current file**,
  including errors outside methods or type declarations. It works without any index,
  even when a malformed declaration cannot be selected. Prefer it after an edit when
  the location of the error is unknown.
- `get_type_diagnostics` takes `relativePath`, the current identifier `line` and `column`,
  and `expectedTypeName`. It checks one class, record, struct, interface, enum or delegate
  declaration, including its members and nested types. Other partial parts and sibling
  declarations are excluded. Both name and position must match; if edits moved or damaged
  the declaration, fall back to `get_file_diagnostics` rather than guessing a new target.
- `validate_method_body` takes `relativePath`, `name` and a proposed `newBody`
  (a complete block or its inner statements), without a prior read or a client-supplied file hash. The detailed
  mode retains location, `snapshotId` and `expectedContentHash` arguments. It validates syntax
  **in memory**, including the proposed block in the context of the method.
  Invalid/incomplete block shapes return a structured `CS4L0001` error.
  Omitted outer braces are added by the same normalization used for writing;
  diagnostic locations refer to the proposed file with those braces in place.
  The default interface analyzes the current file; detailed mode checks supplied provenance.
- `get_method_diagnostics` takes `relativePath`, `line`, `column`, and
  `expectedMethodName` from a previous symbol lookup. It parses **current
  disk contents** directly and does not require any SQLite snapshot. This
  makes it useful immediately after `replace_method_body` (which leaves the
  index stale) or an external editor change. It refuses to silently select
  another method if the name or identifier location no longer match.

All four tools return `isValid`, `diagnostics` (each with `id`, `severity`,
`message` and one-based `location`), `totalDiagnostics`,
`hasMore` and `nextOffset`. They return 50 diagnostics per page by default,
with a maximum of 200; `isValid` is calculated from **all** diagnostics,
not just the requested page. Method tools only report errors and warnings within the
chosen method; they cannot establish that the rest of the file is syntactically valid.

File and type diagnostics also return `relativePath` and, for a type query, `typeLocation`.
The MCP serializer omits null fields, including `nextOffset` on the last page.
They use `codesemantic4llm.yaml` to select an available analyzer and its
configured extensions, rejecting unsupported languages explicitly. They read only the
requested path, without repository discovery or SQLite access; discovery exclusions do
not block an explicit diagnostic request. Paths cannot leave the selected repository or
traverse symlinks. Results describe the current file text; its version is tracked
internally for continuation requests. No source text is returned or persisted.

In the default interface, follow `nextOffset` without supplying any file-version
metadata. A changed file rejects the continuation; start again at offset 0.
With `--expose-indexing` only, responses include `contentHash` and file/type diagnostic
continuations require it as `expectedContentHash`.
For example, call `get_file_diagnostics` with:

```json
{"relativePath":"src/Worker.cs","maxDiagnostics":50}
```

Then follow its `nextOffset` with the same path and scope. Diagnostic
locations are physical, one-based file positions even when `#line` directives are present.
Type and method ranges follow Roslyn's error-recovery tree: missing braces can cause
later code to become part of the recovered declaration. A whole-file check is the reliable
fallback when the structure is broken.

These tools perform **syntax validation only**. For example,
`return Unknown.Customer.Name;` can be syntactically valid despite missing
names or a return-type mismatch. Such issues require project-aware semantic
compilation, which remains a separate planned capability.
Parsing currently uses Roslyn's default C# parse options; it does not evaluate the
target project's language version or conditional compilation symbols, and inactive
preprocessor regions are not checked as active C# code.

Recommended agent workflow:

```text
find_symbols → get_method_body
             → validate_method_body (no source write)
             → replace_method_body (only with serve --allow-write)
             → get_file_diagnostics (all current-file syntax, no snapshot)
               or get_type_diagnostics / get_method_diagnostics (focused scope)
             → inspect git diff → index
```

## Reading and replacing a method body

By default the server is read-only. Pass `--allow-write` explicitly to `serve`
only when you want an MCP client to modify code:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /absolute/path/to/repository --allow-write
```

The argument is a **server startup permission**, not a setting in
`codesemantic4llm.yaml` and not a tool-call argument. Without it,
`replace_method_body` is absent from MCP tool discovery. The default argument-free
server is always read-only. `get_method_body` works in either mode.

1. Select the method with its `relativePath` and `name`. Add its identifier `line` and
   `column` for overloads. Search is optional if the address is already known.
2. Call `get_method_body` with that source address. The response contains `body.text`,
   `totalCharacters` and `nextOffset`; page if the body is large.
3. Optionally call `validate_method_body` with the proposed body to get syntax
   errors before any write, even in read-only mode.
4. Call `replace_method_body` with the same source address and `newBody` containing only a valid
   C# block or its inner statements. Omitted outer braces are added automatically.
   An explicit leading block must already be complete; unbalanced braces and invalid
   control flow are rejected. The replacement can convert an expression-bodied
   method into a block body. It cannot rewrite the signature, attributes or adjacent
   methods, and will not add an implementation to an abstract or interface method.
5. Call `get_file_diagnostics` to check the whole current file without an index refresh
   (or `get_method_diagnostics` for only the written method),
   then inspect the local Git diff and rerun `index`. The old index and its source
   locations are deliberately not altered by the write tool. Only the detailed mode
   exposes `reindexRequired: true`; the default reply contains the method location.

Default edit arguments:

```json
{
  "relativePath": "src/Worker.cs",
  "name": "Worker.Run",
  "newBody": "{ return 42; }"
}
```

`"newBody": "return 42;"` is also accepted and is enclosed in a block before
validation and writing. Supply only body content, without the method signature.

The application validates the branch, revision, file bytes and exact Roslyn declaration
against the preceding body read. Detailed mode also validates the active index generation. The workspace adapter
rejects relative-path traversal and symlinks, rejects non-UTF-8 source files and writes
through a temporary sibling file followed by replacement. It preserves a UTF-8 BOM and
the untouched source text outside the body. The write does **not** compile, run tests,
execute code, commit, or stage changes.

A file-hash check immediately before replacement catches ordinary concurrent edits,
but is not a cross-process atomic compare-and-swap. Avoid concurrent writers to the
same checkout. Prefer reviewing changes and using Git to revert unwanted edits.
