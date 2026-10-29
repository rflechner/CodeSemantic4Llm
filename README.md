# CodeSemantic4Llm

**CodeSemantic4Llm** is an experimental, local-first MCP server for semantic code indexing.
The goal is to let coding agents retrieve small, relevant slices of a repository instead of repeatedly loading large source files into the LLM context.

> Status: modular MCP bootstrap with repository-local YAML configuration and a read-only
> indexing preview. The persistent index pipeline and SQLite schema are not implemented yet.

## Architecture

| Project | Responsibility |
| --- | --- |
| `CodeSemantic4Llm.Domain` | Repository/branch identity, configuration, source locations and shared symbols |
| `CodeSemantic4Llm.Application` | Use cases and ports (analysis, configuration, discovery, index storage) |
| `CodeSemantic4Llm.Analysis.CSharp` | Roslyn-based C# declaration extraction (syntax-level bootstrap) |
| `CodeSemantic4Llm.Analysis.TypeScript` | Reserved adapter for Tree-sitter and TypeScript/Angular |
| `CodeSemantic4Llm.Configuration.Yaml` | Creates, reads and validates configuration at the checkout root |
| `CodeSemantic4Llm.Persistence.Sqlite` | SQLite connection boundary; schema to be designed by language |
| `CodeSemantic4Llm.Infrastructure.Workspace` | Git and safe filesystem integration, exclusion glob matching |
| `CodeSemantic4Llm.Mcp.Server` | Composition root, stdio transport and MCP tools |

See [architecture decisions](docs/architecture.md) and the [repository YAML specification](docs/repository-configuration.md).
The [planned index storage illustration](docs/architecture.md#planned-index-storage) shows
repository-root YAML files, branch snapshots, and shared caches without source-file copies.

## Configure each repository

Create `codesemantic4llm.yaml` at the root of **each repository** with the console command:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  init /absolute-or-relative/path/to/repository --analyzer dotnet-csharp --lang fr
```

The target directory must already exist. Relative paths are resolved from the process's
working directory; quote paths containing spaces. Omit `--lang` to use English (`en`).
Use `--analyzer <id>` once per analyzer to select the YAML's `analyzers` list. The options
may appear in any order after the path. Omit `--analyzer` to select `dotnet-csharp`.
IDs are trimmed, lowercased and deduplicated in first occurrence order. All selected
analyzers must be installed; currently only `dotnet-csharp` is available.
Each analyzer retains its default extensions, and the command adds no custom exclusions.
Existing configuration files are preserved and reported as an error.
Use `init --help` for usage. In Rider, use `init /path/to/repository --lang fr` as the
program arguments for `CodeSemantic4Llm.Mcp.Server`.

You can also create or edit the configuration manually:

```yaml
version: 1
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

Start the MCP server (choose the checkout explicitly):

```shell
CODESEMANTIC4LLM_REPOSITORY_ROOT=/absolute/path/to/repository \
  dotnet run --project src/CodeSemantic4Llm.Mcp.Server
```

The server uses **stdio** for MCP and sends logs to **stderr**.
It exposes `get_server_info`, `inspect_source`, `get_repository_configuration`,
and `preview_repository_index`. Preview tools **do not yet build or persist an index**.

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
`--help` lists the `init` and `preview` commands.

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

1. Define and test the branch-aware SQLite layout and index lifecycle.
2. Implement atomic per-file updates, deletions/renames and incremental maintenance.
3. Add Roslyn semantic models, references and stable symbol identities.
4. Implement TypeScript/Angular analysis using a validated Tree-sitter integration.
5. Connect repository YAML-driven file discovery to actual indexing and targeted search through MCP.

Read [AGENTS.md](AGENTS.md) before making changes.
