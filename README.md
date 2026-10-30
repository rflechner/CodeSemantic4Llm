# CodeSemantic4Llm

**CodeSemantic4Llm** is an experimental, local-first MCP server for semantic code indexing.
The goal is to let coding agents retrieve small, relevant slices of a repository instead of repeatedly loading large source files into the LLM context.

> Status: repository-local YAML configuration, C# declaration and Roslyn-resolved reference
> indexing into SQLite, and bounded MCP queries against saved branch snapshots.
> Reference binding currently uses a lightweight repository-wide compilation rather than
> full MSBuild project resolution; AI-generated descriptions and TypeScript analysis remain planned.

## Architecture

| Project | Responsibility |
| --- | --- |
| `CodeSemantic4Llm.Domain` | Repository/branch identity, configuration, source locations and shared symbols |
| `CodeSemantic4Llm.Application` | Use cases and ports (analysis, configuration, discovery, index storage) |
| `CodeSemantic4Llm.Analysis.CSharp` | Roslyn-based C# declaration extraction (syntax-level bootstrap) |
| `CodeSemantic4Llm.Analysis.TypeScript` | Reserved adapter for Tree-sitter and TypeScript/Angular |
| `CodeSemantic4Llm.Configuration.Yaml` | Creates, reads and validates configuration at the checkout root |
| `CodeSemantic4Llm.Persistence.Sqlite` | Versioned declaration snapshots, atomic publication and bounded queries |
| `CodeSemantic4Llm.Infrastructure.Workspace` | Git and safe filesystem integration, exclusion glob matching |
| `CodeSemantic4Llm.Mcp.Server` | Composition root, stdio transport and MCP tools |

See [architecture decisions](docs/architecture.md) and the [repository YAML specification](docs/repository-configuration.md).
See [indexing and querying a repository](docs/indexing.md) for cache configuration and snapshot behavior.
The [planned index storage illustration](docs/architecture.md#planned-index-storage) shows
repository-root YAML files, branch snapshots, and shared caches without source-file copies.

## Configure each repository

Create `codesemantic4llm.yaml` at the root of **each repository** with the console command:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  init /absolute-or-relative/path/to/repository --name "My Project" --analyzer dotnet-csharp --lang fr
```

The target directory must already exist. Relative paths are resolved from the process's
working directory; quote paths containing spaces. Omit `--lang` to use English (`en`).
Use `--analyzer <id>` once per analyzer to select the YAML's `analyzers` list. The options
may appear before or after the path; `--lang=fr` syntax is also accepted.
Omit `--analyzer` to select `dotnet-csharp`.
Use `--name "My Project"` to write the optional `projectName` display label. It accepts
spaces and Unicode, trims surrounding whitespace and preserves case. Omit it to leave
the project unnamed. The name is independent of the repository path and index cache identity.
IDs are trimmed, lowercased and deduplicated in first occurrence order. All selected
analyzers must be installed; currently only `dotnet-csharp` is available.
Each analyzer retains its default extensions, and the command adds no custom exclusions.
Existing configuration files are preserved and reported as an error.
Use `init --help` for usage. In Rider, use `init /path/to/repository --lang fr` as the
program arguments for `CodeSemantic4Llm.Mcp.Server`.

You can also create or edit the configuration manually:

```yaml
version: 1
projectName: My Project   # Optional display label
analyzers:
  - dotnet-csharp
# Optional: replaces this analyzer's default extension list (.cs).
# fileExtensions:
#   dotnet-csharp: [.cs, .csharp]
semanticIndexing:
  language: en   # Optional: use fr, fr-CH, etc.
exclude: []
```

`**/.git/**`, `**/bin/**`, and `**/obj/**` are always excluded.
Submodules are only excluded when explicitly selected by a rule.
Discovery recursively visits the root and its nested directories, skips symbolic links,
and selects `.cs` files by default for `dotnet-csharp`, ignoring extension case.
The optional `fileExtensions` mapping replaces extensions per enabled analyzer;
include `.cs` in an override to keep it alongside custom extensions. The preview reports
the effective extensions and returns at most 200 candidate paths without reading source contents.
The optional `semanticIndexing.language` selects the language of **future AI-generated
natural-language descriptions**; its default is `en`. It does not affect which source
languages the analyzers support. The value is already validated and reported by the MCP preview,
but natural-language index generation is not implemented yet.
The TypeScript analyzer is planned but not yet registered.

## Build and test

Requires .NET 10 SDK and Git for operations against checked-out repositories.

```shell
dotnet restore CodeSemantic4Llm.slnx
dotnet build CodeSemantic4Llm.slnx
dotnet test CodeSemantic4Llm.slnx
```

The new functional tests use **Reqnroll (successor to SpecFlow)** with xUnit v3.
The `.feature` files are executable examples of the intended indexing behavior.

## Index before starting MCP

After creating the repository YAML, index every matching source file:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  index /absolute-or-relative/path/to/repository
```

The path must identify a Git checkout root on a named branch. Interactive terminals show
Spectre progress for discovery, indexing, verification and publication. Redirected output
contains a plain summary. Unlike preview, indexing has no file-count limit.

Rerunning `index` reuses unchanged declarations and removes stale records after deletions
or renames. Each branch keeps its own completed snapshot. Failures and cancellation preserve
the previous snapshot. SQLite stores derived metadata only, outside the source checkout.
Set `CODESEMANTIC4LLM_INDEX_ROOT` to an absolute cache directory to override the default
`LocalApplicationData/CodeSemantic4Llm/indexes`; use the same setting when starting MCP.

Start the MCP server (choose the checkout explicitly):

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /absolute-or-relative/path/to/repository
```

`serve [repository-path]` accepts an existing directory. The argument overrides
`CODESEMANTIC4LLM_REPOSITORY_ROOT`; omitting it preserves environment-based selection.
Relative paths are resolved from the process working directory. In Rider, use
`serve /path/to/repository` as the program arguments. Startup does not index the project.

The server uses **stdio** for MCP and sends logs to **stderr**.
It exposes `get_server_info`, `inspect_source`, `get_repository_configuration`,
`preview_repository_index`, `get_repository_index_status`, `find_symbols`, `get_type_members`,
and `find_usages`.
`find_symbols` returns saved declaration locations by exact, case-sensitive simple name
(20 results by default, at most 200). Run `index` again after source edits. MCP startup and
queries do not rebuild the index; preview remains read-only.

To ask for a class's methods, first locate its declaration with `find_symbols`, then call
`get_type_members` with that declaration's relative path, line, column, snapshot ID and
`kind: "method"`. Results include overloads as separate locations and support pagination.
Only members declared directly in the selected type declaration are returned: inherited
methods and other parts of a partial type are not merged. See the
[member navigation example](docs/indexing.md#listing-a-classs-methods).

To find callers of a method or references to a class, first select its declaration with
`find_symbols`, then call `find_usages` with the same relative path, line, column and
`snapshotId`. The tool returns bounded, paginated source locations (50 by default,
200 maximum) from SQLite without parsing code during the MCP request. Unlike simple text
search, Roslyn distinguishes overloads and same-named methods. References are recalculated
during indexing when files change; unresolved or ambiguous references are omitted. The
first implementation does not evaluate project files or external dependency graphs, so
results may be incomplete for multi-project repositories. See the
[usage search example](docs/indexing.md#finding-usages-of-a-method-or-type).

When upgrading an older cache, rerun `index` for each branch you need to
query. Schema 3 adds reference relationships; existing declarations remain readable
until that branch has been reindexed. Restart the MCP server so clients can discover
`find_usages`.

### Optional method-level source editing

The MCP server is **read-only by default**. To allow a connected agent to change an
indexed method body, explicitly start this server process with:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /path/to/repository --allow-write
```

`get_method_body` is always available: after `find_symbols` or `get_type_members`,
it reads only the selected method's body (12,000 characters per page by default) and
returns the source-file `contentHash`. `replace_method_body` is **advertised only**
when `--allow-write` is set. It accepts the method's exact indexed location and
`snapshotId`, the `contentHash` from `get_method_body`, and a replacement C# block
including braces. It changes **only that method's executable body**, without requiring
a whole-file text replacement by the agent.

For faster feedback, `validate_method_body` accepts the same declaration,
`snapshotId`, `contentHash`, and proposed block, then reports Roslyn syntax errors
**without writing anything**. After an edit, `get_method_diagnostics` can inspect the
current method directly on disk, even before reindexing. Pass the declaration path,
line, column and expected method name; no snapshot is required. Both tools are available
in read-only mode and return bounded diagnostics with C# error IDs and source locations.
**These are syntax checks, not compilation or semantic type checks.**

The write is rejected if the index or working-tree file is stale, the syntax is invalid,
or the method has no body. Writes are restricted to the operator-selected checkout;
symlinks and traversal are rejected. This is a local source edit, not an index update:
**run `index` again after any successful edit**, and review your Git diff before committing.
Only UTF-8 source files are supported for editing in this first version.
See [the editing workflow](docs/indexing.md#reading-and-replacing-a-method-body).

### MCP client configuration

For VS Code's `.vscode/mcp.json`, use a stdio server with the repository in `args`:

```json
{
  "servers": {
    "codeSemantic4Llm": {
      "type": "stdio",
      "command": "dotnet",
      "args": [
        "/absolute/path/to/CodeSemantic4Llm/src/CodeSemantic4Llm.Mcp.Server/bin/Debug/net10.0/CodeSemantic4Llm.Mcp.Server.dll",
        "serve",
        "${workspaceFolder}"
      ]
    }
  }
}
```

Build the server first and replace the DLL path. Launching the built DLL keeps build output
out of the MCP protocol stream. `${workspaceFolder}` selects the open workspace; an absolute
repository path also works. This example follows the [VS Code MCP configuration format](https://code.visualstudio.com/docs/agents/reference/mcp-configuration).
Other clients may use a different outer JSON structure; pass the same executable arguments.
If indexing uses a custom `CODESEMANTIC4LLM_INDEX_ROOT`, set that variable in the client's
server environment too.

## Preview a local repository from Rider or a terminal

To display discovered files immediately, run the console preview with the path to a
checkout containing `codesemantic4llm.yaml`:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  preview /absolute/path/to/repository --max-files 200
```

In Rider, open **Run > Edit Configurations**, create a **.NET Project** configuration,
select `CodeSemantic4Llm.Mcp.Server`, and set **Program arguments** to
`preview /absolute/path/to/repository --max-files 200`. Run or Debug displays the
effective settings and selected files in the console. You can set a breakpoint in
`FileSystemRepositoryFileEnumerator.EnumerateFiles` to inspect traversal.
See [Rider's run configuration documentation](https://www.jetbrains.com/help/rider/Run_Debug_Configuration_dotNet_Project.html).

The preview uses the same discovery rules as MCP and exits after displaying its result.
The default limit is 50 and the maximum is 200; the output indicates when more files exist.
It does not read source contents, execute the target repository, or persist an index.
Running without a command starts the MCP server and waits for client requests.
`--help` lists the `init`, `preview`, `index`, and `serve` commands. `serve` explicitly starts
the same MCP server as an invocation without arguments, with optional repository selection.

Console commands use Spectre.Console.Cli settings for argument parsing and generated
help, with Spectre.Console for output. Help and results go to stdout; errors go to stderr.
Unknown commands and options fail without starting the MCP server.

## Dependencies

NuGet packages use **Central Package Management**:
- Versions live exclusively in `Directory.Packages.props`.
- Each `PackageReference` omits `Version`.
- Shared compilation settings are in `Directory.Build.props`.
- The .NET SDK and Microsoft Testing Platform runner are configured via `global.json`.

## Indexing principles

- The original Git checkout is the source of truth. Never duplicate source trees.
- Keep repository and Git branch indexes distinct; do not use unvalidated branch names as directories.
- Persist derived metadata, symbol identity, locations, hashes and relationships, never source contents.
- Share cross-language query concepts while allowing separate entities and persistence tables by language.
- Prefer incremental updates, deterministic queries and compact MCP responses.
- Keep compiler and SQLite implementations outside the domain and application layers.

## Planned milestones

1. Add project-aware semantic compilation and stable, cross-project symbol identities.
2. Implement TypeScript/Angular analysis using a validated Tree-sitter integration.
3. Add source excerpts validated against indexed content hashes and paginated navigation.
4. Add dependency-aware invalidation and reuse across branches as semantic analysis expands.
5. Generate semantic descriptions and embeddings with explicit model and language invalidation.

Read [AGENTS.md](AGENTS.md) before making changes.
