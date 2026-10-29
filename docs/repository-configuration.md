# Repository-local YAML configuration

Each checkout to be indexed contains its own `codesemantic4llm.yaml` **at the repository root**.
The server does not search parent folders for shared defaults; each repository owns its configuration.

## Example

```yaml
version: 1

analyzers:
  - dotnet-csharp
  # - typescript  # Planned, not yet implemented

exclude:
  - submodules/**
  - third_party/
  - "**/*.g.cs"
```

- `version` must be `1`.
- `analyzers` is a required, non-empty list of installed analyzer IDs. Currently only `dotnet-csharp` is registered.
- `exclude` is optional. Rules add to, rather than replace, the built-in defaults.
- Git submodules are **not** excluded automatically; list their checkout paths if they should be skipped.
- Unknown YAML properties, duplicate keys, missing configuration, and unknown/unimplemented analyzers fail explicitly.

## Exclusion rules

Rules use **repository-relative** paths and glob patterns, with `/` as separator:

| Rule | Meaning |
| --- | --- |
| `submodules/**` | Everything below the root `submodules` folder |
| `submodules/` | Equivalent to `submodules/**` |
| `third_party` | Equivalent to `third_party/**` |
| `**/generated/**` | Any `generated` directory at any depth |
| `**/*.g.cs` | Generated C# files matching the pattern, without excluding their entire directory |

Always excluded at every depth: `**/.git/**`, `**/bin/**`, and `**/obj/**`.

The directory walker also skips symlinks/junctions rather than following them outside the
checkout or risking traversal cycles. Exclusion rules cannot be absolute paths or contain
parent-directory segments (`..`).

## Running a read-only preview through MCP

The MCP server is explicitly restricted to a repository selected by the operator:

```sh
CODESEMANTIC4LLM_REPOSITORY_ROOT=/absolute/path/to/checkout \
  dotnet run --project src/CodeSemantic4Llm.Mcp.Server
```

For PowerShell:

```powershell
$env:CODESEMANTIC4LLM_REPOSITORY_ROOT = "D:\\source\\my-project"
dotnet run --project src/CodeSemantic4Llm.Mcp.Server
```

The `get_repository_configuration` MCP tool returns the effective configuration.
The `preview_repository_index` MCP tool selects a **bounded** set of candidate source files
without reading their contents or writing a database. If another installed analyzer is
listed in the YAML, the preview fails rather than silently ignoring its files.

`inspect_source` remains an explicit, in-memory snippet analysis tool and does not
use repository YAML settings.

## Executable specifications

Functional behavior is described in
`tests/CodeSemantic4Llm.FunctionalTests/Features/RepositoryConfiguration.feature`
using [Reqnroll](https://reqnroll.net/) and xUnit v3. The tests create temporary
repositories, write YAML, create sample files, run the real loader and directory walker,
and assert which files would be indexed. They do not require Git, MCP clients or SQLite.

Run all tests with `dotnet test CodeSemantic4Llm.slnx`.
