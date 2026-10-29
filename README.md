# CodeSemantic4Llm

**CodeSemantic4Llm** is an experimental, local-first MCP server for semantic code indexing.
The goal is to let coding agents retrieve small, relevant slices of a repository instead of repeatedly loading large source files into the LLM context.

> Status: architecture and executable MCP bootstrap. The index pipeline and SQLite schema are not implemented yet.

## Architecture

| Project | Responsibility |
| --- | --- |
| `CodeSemantic4Llm.Domain` | Repository/branch identity, source locations, shared symbol vocabulary |
| `CodeSemantic4Llm.Application` | Use cases and ports (language analysis, Git branch, index storage) |
| `CodeSemantic4Llm.Analysis.CSharp` | Roslyn-based C# declaration extraction (syntax-level bootstrap) |
| `CodeSemantic4Llm.Analysis.TypeScript` | Reserved adapter for Tree-sitter and TypeScript/Angular |
| `CodeSemantic4Llm.Persistence.Sqlite` | SQLite connection boundary; schema to be designed by language |
| `CodeSemantic4Llm.Infrastructure.Workspace` | Working-tree and Git integration |
| `CodeSemantic4Llm.Mcp.Server` | Composition root, stdio transport, MCP tools |

See [architecture decisions and dependency graph](docs/architecture.md).

## Build and test

Requirements: .NET 10 SDK, and Git for operations against checked-out repositories.

```shell
dotnet restore CodeSemantic4Llm.slnx
dotnet build CodeSemantic4Llm.slnx
dotnet test CodeSemantic4Llm.slnx
```

Start the MCP host:

```shell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server
```

The server uses **stdio** for MCP communication and sends logs to **stderr**. It currently exposes `get_server_info` and `inspect_source` (in-memory C# syntax inspection, not repository indexing).

## Dependencies

NuGet packages use **Central Package Management**:
- Versions live exclusively in `Directory.Packages.props`.
- Each `PackageReference` omits the `Version` attribute.
- Shared compiler settings live in `Directory.Build.props`.
- The .NET SDK version policy lives in `global.json`.

## Indexing principles

- Original source files remain in their existing Git checkout. **Never duplicate source trees or indexed files.**
- Indexes are keyed by **repository and Git branch**; branch names must not be used as unvalidated directory names.
- Persist derived metadata, symbol identities, paths, hashes and relationships, never original source contents.
- Keep a small shared cross-language vocabulary while preserving language-specific entities and persistence tables.
- Prefer incremental updates, deterministic queries, and compact MCP responses.
- Keep parser implementations and SQLite details out of the domain and application layers.

## Planned milestones

1. Define and test the branch-aware SQLite layout and index lifecycle.
2. Implement atomic per-file updates, deletion/rename handling, and incremental index maintenance.
3. Add Roslyn semantic models, references and stable symbol identities.
4. Implement TypeScript/Angular analysis using a validated Tree-sitter integration.
5. Expose repository-scoped indexing and targeted search through MCP.

Read [AGENTS.md](AGENTS.md) before making changes.
