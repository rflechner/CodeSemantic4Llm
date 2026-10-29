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

Currently `dotnet-csharp` extracts syntax declarations using Roslyn. Stored results
include the language, declaration kind, simple and qualified names, relative path and
one-based line/column. These are declaration locations, not compiler-backed semantic
identities. References, cross-file resolution, AI descriptions and embeddings are not
implemented. `semanticIndexing.language` is recorded for future generated text.

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

The versioned schema stores only derived declarations and lookup/invalidation metadata.
It is a regenerable cache, not a stable interchange format or a source backup. Unsupported
schema versions fail without migration; select a fresh cache directory and rerun `index`.
Deleting an unused cache directory is sufficient to discard its derived data.

## Reindexing and publication

Rerun the same command after edits. Content hashes, analyzer identity/version and a
configuration fingerprint decide whether prior declarations can be reused. New and
changed files are analyzed; deleted files and old rename locations disappear on publication.
Changes to analyzer selection, extensions, exclusions or semantic language invalidate reuse.
All selected files are read and hashed even when their declarations can be reused.

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

A missing branch snapshot or a different HEAD produces an error asking the operator to
run `index`. Queries do not rehash the working tree or reload YAML to establish freshness;
rerun `index` after source/configuration changes, even if HEAD has not changed. Locations
refer to the recorded snapshot and can become stale after edits. Existing preview tools
remain bounded and read-only.
