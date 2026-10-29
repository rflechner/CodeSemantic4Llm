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
| `CodeSemantic4Llm.Configuration.Yaml` | Reads and validates configuration from the checkout root |
| `CodeSemantic4Llm.Persistence.Sqlite` | SQLite connection boundary; schema to be designed by language |
| `CodeSemantic4Llm.Infrastructure.Workspace` | Git and safe filesystem integration, exclusion glob matching |
| `CodeSemantic4Llm.Mcp.Server` | Composition root, stdio transport and MCP tools |

See [architecture decisions](docs/architecture.md) and the [repository YAML specification](docs/repository-configuration.md).
The [planned index storage illustration](docs/architecture.md#planned-index-storage) shows
repository-root YAML files, branch snapshots, and shared caches without source-file copies.

## Configure each repository

Create `codesemantic4llm.yaml` at the root of **each repository**:

```yaml
version: 1
analyzers:
  - dotnet-csharp
semanticIndexing:
  language: en   # Optional: use fr, fr-CH, etc.
exclude:
  - submodules/**
```

`**/.git/**`, `**/bin/**`, and `**/obj/**` are always excluded.
Submodules are only excluded when explicitly selected by a rule.
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
