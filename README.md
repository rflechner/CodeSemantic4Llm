# CodeSemantic4Llm

**CodeSemantic4Llm** is an experimental, local-first MCP server for semantic code indexing.
The goal is to let coding agents retrieve small, relevant slices of a repository instead of repeatedly loading large source files into the LLM context.

> Status: repository-local YAML configuration, C# declaration and Roslyn-resolved reference
> indexing into SQLite, and bounded MCP queries against saved branch snapshots.
> Reference binding currently uses a lightweight repository-wide compilation rather than
> full MSBuild project resolution; optional OpenAI-compatible LLM summaries are implemented
> for C# methods and types, with local FTS5/BM25 keyword search over stored
> names and summaries. Embeddings and TypeScript analysis remain planned.

## Install as a .NET tool

The CLI can be distributed as the **`CodeSemantic4Llm.Mcp.Server`** NuGet .NET
Tool through `https://nuget.pkg.github.com/rflechner/index.json`.
Its executable command is **`codesemantic4llm`**, supporting `init`,
`preview`, `index`, `dashboard`, and `serve`.

For GitHub Packages PAT authentication, project-local tool manifests,
global installs, and copy-ready MCP JSON for **VS Code**, **GitHub Copilot
CLI** and **Claude Desktop**, follow the
[.NET tool installation and MCP tutorial](docs/dotnet-tool.md).
The registry requires a classic `read:packages` PAT even for public
packages; the published package must also be made public separately
if everyone should have access.

```sh
# Once the feed is registered and authenticated (see the tutorial):
dotnet tool install --global CodeSemantic4Llm.Mcp.Server
codesemantic4llm --help
```

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
  llm:
    enabled: false  # Opt in per repository
    # baseUrl: http://127.0.0.1:1234/v1
    # model: my-local-model
    # apiKey: your-api-key
    # apiKeyEnvironmentVariable: CODESEMANTIC4LLM_LLM_API_KEY
exclude: []
```

`**/.git/**`, `**/bin/**`, and `**/obj/**` are always excluded.
Submodules are only excluded when explicitly selected by a rule.
Discovery recursively visits the root and its nested directories, skips symbolic links,
and selects `.cs` files by default for `dotnet-csharp`, ignoring extension case.
The optional `fileExtensions` mapping replaces extensions per enabled analyzer;
include `.cs` in an override to keep it alongside custom extensions. The preview reports
the effective extensions and returns at most 200 candidate paths without reading source contents.
The optional `semanticIndexing.language` selects the language of AI-generated summaries;
its default is `en`. It does not affect which source languages the analyzers support.
To enable generated summaries, set `semanticIndexing.llm.enabled: true` and supply an OpenAI-compatible
`baseUrl` and `model` in that repository's YAML. Existing C# XML `<summary>` documentation is
indexed locally even with LLM calls disabled. By default, only declarations without a usable
summary need generation; `semanticIndexing.llm.forceGeneration: true` overrides that preference
when the LLM is enabled. An optional `apiKey` supplies a Bearer token
directly and takes priority over `apiKeyEnvironmentVariable`; blank keys use the environment
fallback. Inline keys are omitted from MCP configuration responses.
The model receives entire C# methods
(including XML doc comments) or entire type declarations. Each generated summary is limited
to 10 lines and is available to MCP clients and the dashboard. Source is not copied
into SQLite, but **the selected LLM endpoint does receive source code**.
See [LLM configuration and data safety](docs/repository-configuration.md#optional-per-repository-llm-summaries).
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

## Browse every cached index in a dashboard

Launch the local, read-only HTTP explorer to inspect all indexed checkouts, Git
branches, declarations, files, kinds and resolved references:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- dashboard --port 5080
```

Open **http://127.0.0.1:5080/**. The dashboard serves its own offline UI,
never reads source contents, and does not start the MCP transport or reindex.
Run `index` on each checkout/branch you want to explore first. It uses the
same optional `CODESEMANTIC4LLM_INDEX_ROOT` environment setting as MCP.
See [dashboard documentation](docs/dashboard.md) for exploration features and
the internal read-only JSON API.

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
or renames. FTS5 search entries follow the same atomic snapshot lifecycle. Each branch keeps its own completed snapshot. Failures and cancellation preserve
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

Every exposed MCP tool description identifies its project: `In project TinyHarness, ...`.
Set `projectName: TinyHarness` in that checkout's YAML to choose the label; otherwise the
directory name is used. This helps agents distinguish multiple connected project servers.
Refresh the client's tool list after changing the name.

The server uses **stdio** for MCP and sends logs to **stderr**.
To retain logs for investigating failed tool calls, add
`--log-file /absolute/path/to/codesemantic4llm.jsonl` to `serve`.
The file includes complete tool parameters and responses, including source snippets.
See [file logging and troubleshooting](docs/logging.md) for examples and log fields.
By default it exposes code-focused tools: `get_server_info`,
`find_symbols`, `search_code`, `get_type_members`, `find_usages`, `get_method`, `get_method_body`,
`validate_method_body`, and the file/type/method diagnostic tools. Tool descriptions,
parameters and responses hide indexing, snapshot details and file hashes. Select declarations
by `relativePath` and `name` (simple or qualified), adding the identifier `line` and `column`
only when needed to distinguish overloads or other ambiguous declarations. No temporary
symbol token or preliminary search is required.
Tool-result JSON keeps source punctuation and Unicode readable: quotes use the usual
JSON `\"` escape, while `+`, `<`, `>`, apostrophes and accented letters remain literal.
The server does not add HTML-style Unicode escapes such as `\u0022` or `\u002B` to
represent those characters. Literal escape sequences already present in source remain
unchanged; clients should parse the JSON rather than decoding source escapes themselves.
Qualified names may omit leading namespaces or enclosing types: `CliApplication.RunAsync`
matches `TinyHarness.Cli.CliApplication.RunAsync` when unique in the requested file.
Matching remains case-sensitive and respects complete dot-separated name segments.

`find_symbols` matches exact, case-sensitive simple names (20 results by default,
at most 200). **`search_code`** accepts a natural-language question such as
`"where does the repository index get published?"`, ranks results using local
SQLite FTS5/BM25 across symbol names and stored documentation or LLM summaries, and returns
source locations and optional descriptions. No LLM server is required at search
time. This is keyword-based relevance, not embedding similarity; see the
[FTS5 search guide](docs/indexing.md#searching-saved-summaries-with-fts5). To list a class's methods, pass its file path and name to `get_type_members`
with `kind: "method"`. To find callers or type references, use the same source-address
arguments with `find_usages`. Both support pagination, defaulting to
50 results with a maximum of 200. Overloads stay distinct, partial declarations
are selected separately, and inherited members are not included. Reference results
omit unresolved bindings and do not evaluate project dependency graphs.

Indexing remains an operator responsibility: run `index` after source changes.
MCP startup and queries do not rebuild the index. Internal branch, snapshot and
file-version checks remain internal. To add operator tools
`get_repository_configuration`, `preview_repository_index` and
`get_repository_index_status`, use:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /path/to/repository --expose-indexing
```

`--expose-indexing` does not enable source writes and can be combined with
`--allow-write`. Code tool arguments and results remain the same in both modes:
no file hashes or snapshot IDs are required. Index status alone exposes snapshot metadata.
Restart the server and refresh the client's tool list when changing modes.
See the [MCP interface documentation](docs/indexing.md#querying-through-mcp).

When upgrading an older cache, rerun `index` for each branch you need to
query. Schema 3 adds reference relationships; existing declarations remain readable
until that branch has been reindexed. Restart the MCP server so clients can discover
`find_usages`.

### Optional method-level source editing

The MCP server is **read-only by default**. To allow a connected agent to change a
C# method, explicitly start this server process with:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /path/to/repository --allow-write
```

`get_method_body` is always available and reads current source directly, even without
navigation data or a previous search. It returns only the selected method's body (12,000 characters per page by default) and
keeps the file version internally for pagination. Pass each returned `nextOffset` unchanged to continue. `replace_method_body` is **advertised only**
when `--allow-write` is set. Pass the file path and method name, with a replacement C# body
with or without outer braces: `return 42;` and `{ return 42; }` are both accepted.
The server adjusts indentation to the target method, preserving relative indentation inside
the supplied body and whitespace inside multiline strings. Code line endings follow the file.
It changes **only that method's executable body**, without requiring
a whole-file text replacement by the agent.

**No prior read is required for editing.** `get_method` and `get_method_body` are optional
inspection tools, not prerequisites. `replace_method` accepts a complete declaration
including attributes, signature and body, selected by its current path and name.
Both write tools work without navigation data, after a server restart, and with
`--expose-indexing`, using the same arguments and no caller-managed version metadata.

**Call `replace_method_body` or `replace_method` directly to edit.** Both run syntax
validation before writing, in the same request. If validation fails, the file is unchanged and
the MCP error returns up to 10 diagnostics with C# error IDs, messages and positions in
the proposed file, plus the number of remaining errors. Correct the body and retry that
same tool; a separate validation call is unnecessary. Full-method errors include malformed
signatures and unterminated strings, such as `CS1010: Newline in constant`, instead of
generic declaration-shape guidance. Successful replies stay compact.

Use `validate_method_body` only when you want diagnostics **without attempting a write**,
including on read-only servers. It accepts the same proposed body and supports pagination.
After an edit, `get_method_diagnostics` can inspect the
current method directly on disk, even before reindexing. Pass the declaration path,
line, column and expected method name; no snapshot is required. Both tools are available
in read-only mode and return bounded diagnostics with C# error IDs and source locations.

To catch errors **anywhere in the current file**, call `get_file_diagnostics` with just
`{"relativePath":"src/Worker.cs"}`. No index, declaration position or proposed code is
needed, so it also works when syntax errors prevent selecting a method. For a single
class or other type, `get_type_diagnostics` accepts the current identifier's `relativePath`,
`line`, `column` and `expectedTypeName`; it includes members and nested types in that
declaration. Both tools use the repository YAML's analyzer and extension settings and
return up to 50 diagnostics by default (maximum 200). Pass each returned `nextOffset`
unchanged as `offset` to retrieve more. The server tracks the file version internally
and rejects continuations after an edit. Restart the MCP server and refresh the client's tool list to discover these tools.

**These are syntax checks, not compilation or semantic type checks.**

Each write targets the source currently on disk. It rejects branch, revision or file
changes detected during the write, invalid syntax, and body edits on methods without bodies.
It does not compare the source to any earlier model read; changes made before the call
are part of the current source being replaced. Writes are restricted to the operator-selected checkout;
symlinks and traversal are rejected. This is a local source edit, not an index update:
**run `index` again after any successful edit**, and review your Git diff before committing.
Only UTF-8 source files are supported for editing in this first version.
See [the editing workflow](docs/indexing.md#reading-and-replacing-a-method-body).

### MCP client configuration (when running from a source build)

For the **installed .NET tool** instead of a manually built DLL, see the
[VS Code, Copilot CLI and Claude Desktop examples](docs/dotnet-tool.md#4-configure-an-mcp-client).
The example below is specifically for running from a **source build**.

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
`--help` lists the `init`, `preview`, `index`, `dashboard`, and `serve` commands. `serve` explicitly starts
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
5. Add semantic search embeddings and finer-grained summary invalidation independent of declaration extraction.

Read [AGENTS.md](AGENTS.md) before making changes.
