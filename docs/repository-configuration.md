# Repository-local YAML configuration

Each checkout to be indexed contains its own `codesemantic4llm.yaml` **at the repository root**.
The server does not search parent folders for shared defaults; each repository owns its configuration.
The YAML file does not belong in the central index cache. See the
[planned index storage illustration](architecture.md#planned-index-storage) for the separation
between original checkouts, branch snapshots, and shared derived-data caches.

## Example

```yaml
version: 1

analyzers:
  - dotnet-csharp
  # - typescript  # Planned, not yet implemented

semanticIndexing:
  language: fr   # Optional; defaults to en

exclude:
  - submodules/**
  - third_party/
  - "**/*.g.cs"
```

- `version` must be `1`.
- `analyzers` is a required, non-empty list of installed analyzer IDs. Currently only `dotnet-csharp` is registered.
- `semanticIndexing.language` is optional and defaults to `en` (English).
- `exclude` is optional. Rules add to, rather than replace, the built-in defaults.
- Git submodules are **not** excluded automatically; list their checkout paths if they should be skipped.
- Unknown YAML properties, duplicate keys, missing configuration, and unknown/unimplemented analyzers fail explicitly.

## Language of semantic descriptions

The optional `semanticIndexing.language` setting selects the human language in which
**future AI-generated semantic descriptions, summaries, and natural-language index text**
will be written. It is separate from `analyzers`, which choose the programming languages
to parse. For example, `dotnet-csharp` can analyze French-named C# code while descriptions
are generated in English, French or another supported language.

- `en`: English (default when `semanticIndexing` or `language` is omitted)
- `fr`: French
- `fr-CH`: French (Switzerland)
- `en-US`: English (United States)
- `zh-Hant-TW`: Chinese, traditional script (Taiwan)

Codes are case-normalized using the supported subset of **BCP 47**: a two- or three-letter
language, optional four-letter script, and optional two-letter/three-digit region.
Incorrect formats such as `english`, `fr_CH` or an empty value fail validation.
The code is validated syntactically; actual language support will depend on the semantic
generation model when that feature is implemented.

**Current status:** the option is parsed, validated and included in configuration and
preview responses. Natural-language descriptions, vector generation, and index invalidation
on a language change are not yet implemented. When the semantic indexing pipeline is added,
changing the configured language must invalidate and regenerate any derived text that depends
on that language, without changing the original source-code analysis or copying source files.

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

The `get_repository_configuration` MCP tool returns the effective configuration, including
`semanticIndexing.language` (default `en`). The preview also reports this value as
`semanticIndexingLanguage`.
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
