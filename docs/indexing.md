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
AI descriptions and embeddings remain planned. `semanticIndexing.language` is recorded
for future generated text.

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
or a source backup. Schema 2 introduced type membership, and schema 3 adds a compact
reference table indexed by target declaration position. Running `index` upgrades version-one
and version-two caches on the write path; the old published snapshot remains intact until
atomic publication. An earlier branch may still return its declarations, but cannot
answer `find_usages` until reindexed. An interrupted run preserves the older published
snapshot. MCP reads never migrate storage.

Unsupported schema versions fail without changes; select a fresh cache directory and rerun
`index`. Deleting an unused cache directory is sufficient to discard its derived data.

## Reindexing and publication

Rerun the same command after edits. Content hashes, analyzer identity/version and a
configuration fingerprint decide whether prior declarations can be reused. New and
changed files are analyzed; deleted files and old rename locations disappear on publication.
Changes to analyzer selection, extensions, exclusions or semantic language invalidate reuse.
All selected files are read and hashed even when their declarations can be reused.
References are global semantic relationships: when any selected file is added, deleted
or changed, all C# references are resolved again against the new compilation. Only an
identical, fully reused set of files may copy all reference rows from the previous
snapshot. This prevents an unchanged caller from retaining a stale edge after its
target is edited.

One SQLite transaction stages the replacement. Publication switches the branch snapshot
and removes its old generation atomically. Other branches retain their snapshots. A failed
or canceled run rolls back; readers continue to see the last completed snapshot. A first
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
  `contentHash` from `get_method_body` and a complete C# block. No tool call or
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

Then call `get_type_members`, copying the selected class's `location` and the response's
`snapshot.snapshotId` (the following numbers are illustrative):

```json
{
  "relativePath": "src/Worker.cs",
  "line": 4,
  "column": 14,
  "snapshotId": 2,
  "kind": "method",
  "maxResults": 50,
  "offset": 0
}
```

The result contains the selected type, its matching members, snapshot metadata and
pagination information. Overloads remain distinct by source location. Methods of nested
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
homonyms and overloads. Then call `find_usages` (numbers are illustrative):

```json
{
  "relativePath": "src/Worker.cs",
  "line": 12,
  "column": 17,
  "snapshotId": 3,
  "maxResults": 50,
  "offset": 0
}
```

The result contains `target`, `snapshot`, `usages` (each a relative path, line
and column), `hasMore` and `nextOffset`. For a class, usages include explicitly
written type names such as `new Worker()`, type annotations, base classes and
type arguments when Roslyn resolves them. For a method, overloads and extension
method calls are matched to the declared method rather than its simple name.
An unresolvable use, dynamic call or reflective invocation is not inferred.
No source bodies are read during `find_usages`; indexing must be rerun after edits.


## Checking a method's syntax

Two read-only MCP tools use Roslyn's C# parser without starting MSBuild, running
application code or writing any files:

- `validate_method_body` takes the declaration's `relativePath`, `line`,
  `column`, `snapshotId`, the `expectedContentHash` from `get_method_body`,
  and a proposed complete `newBody` (including braces). It validates syntax
  **in memory**, including the proposed block in the context of the method.
  Invalid/incomplete block shapes return a structured `CS4L0001` error.
  Changes to the index or file since the last read are rejected.
- `get_method_diagnostics` takes `relativePath`, `line`, `column`, and
  `expectedMethodName` from a previous symbol lookup. It parses **current
  disk contents** directly and does not require any SQLite snapshot. This
  makes it useful immediately after `replace_method_body` (which leaves the
  index stale) or an external editor change. It refuses to silently select
  another method if the name or identifier location no longer match.

Both tools return `isValid`, `diagnostics` (each with `id`, `severity`,
`message` and one-based `location`), `contentHash`, `totalDiagnostics`,
`hasMore` and `nextOffset`. They return 50 diagnostics per page by default,
with a maximum of 200; `isValid` is calculated from **all** diagnostics,
not just the requested page. Only errors and warnings within the chosen
method are reported, not diagnostics from unrelated methods.

These tools perform **syntax validation only**. For example,
`return Unknown.Customer.Name;` can be syntactically valid despite missing
names or a return-type mismatch. Such issues require project-aware semantic
compilation, which remains a separate planned capability.

Recommended agent workflow:

```text
find_symbols → get_method_body
             → validate_method_body (no source write)
             → replace_method_body (only with serve --allow-write)
             → get_method_diagnostics (current file, no snapshot)
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

1. Index the checkout and locate the exact method with `find_symbols` or
   `get_type_members`. Select its declaration position, not just its name (overloads
   may share names).
2. Call `get_method_body` with the method's `relativePath`, `line`, `column`,
   and `snapshotId`. The response contains `body.text`, `contentHash`,
   `totalCharacters` and `nextOffset`; page if the body is large.
3. Optionally call `validate_method_body` with the proposed block to get syntax
   errors before any write, even in read-only mode.
4. Call `replace_method_body` with the same location/snapshot, the exact
   `expectedContentHash` obtained above, and `newBody` containing only a valid
   C# block *including braces*. The replacement can convert an expression-bodied
   method into a block body. It cannot rewrite the signature, attributes or adjacent
   methods, and will not add an implementation to an abstract or interface method.
5. Call `get_method_diagnostics` to check the written method without an index refresh,
   then inspect the local Git diff and rerun `index`. The old index and its source
   locations are deliberately not altered by the write tool; the reply sets
   `reindexRequired: true`.

Example edit arguments (locations and hashes are illustrative):

```json
{
  "relativePath": "src/Worker.cs",
  "line": 12,
  "column": 17,
  "snapshotId": 3,
  "expectedContentHash": "THE_SHA256_FROM_GET_METHOD_BODY",
  "newBody": "{ return 42; }"
}
```

The application validates the selected branch, active index generation, recorded HEAD,
current file hash and exact Roslyn method declaration location. The workspace adapter
rejects relative-path traversal and symlinks, rejects non-UTF-8 source files and writes
through a temporary sibling file followed by replacement. It preserves a UTF-8 BOM and
the untouched source text outside the body. The write does **not** compile, run tests,
execute code, commit, or stage changes.

A file-hash check immediately before replacement catches ordinary concurrent edits,
but is not a cross-process atomic compare-and-swap. Avoid concurrent writers to the
same checkout. Prefer reviewing changes and using Git to revert unwanted edits.
