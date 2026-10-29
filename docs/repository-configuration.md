# Repository-local YAML configuration

Each checkout to be indexed contains its own `codesemantic4llm.yaml` **at the repository root**.
The server does not search parent folders for shared defaults; each repository owns its configuration.
The YAML file does not belong in the central index cache. See the
[planned index storage illustration](architecture.md#planned-index-storage) for the separation
between original checkouts, branch snapshots, and shared derived-data caches.

## Create the default configuration

From the CodeSemantic4Llm checkout, run:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  init /absolute-or-relative/path/to/repository --analyzer dotnet-csharp --lang fr-CH
```

`init <repository-path> [--lang <language-tag>] [--analyzer <analyzer-id>]...`
creates `codesemantic4llm.yaml` directly in the specified existing directory.
Absolute paths and paths relative to the process's
working directory are accepted, including `.` and `..`; quote paths containing spaces.
The command does not create missing directories or require Git metadata, and does not
start an MCP session, discover source files, or build an index.

When both options are omitted, the generated defaults are:

```yaml
version: 1
analyzers:
  - dotnet-csharp
semanticIndexing:
  language: en
exclude: []
```

`--lang` selects `semanticIndexing.language`, using the same validation and case
normalization as the YAML loader (for example, `FR-ch` becomes `fr-CH`). Its default
is `en`.

Repeat `--analyzer <analyzer-id>` to select multiple analyzers. An explicit selection
replaces the default `dotnet-csharp` selection. IDs are trimmed, lowercased and
deduplicated, retaining the order of their first occurrence. Every selected analyzer
must be available before any configuration is written; unknown or unimplemented IDs
fail with the available analyzer list. Currently only `dotnet-csharp` is installed;
`typescript` remains planned and cannot be selected yet.

Options may appear in any order after the repository path, including `--lang` between
two `--analyzer` options. Each occurrence requires one value; only `--analyzer` is
repeatable. Empty analyzer IDs, missing values, unknown options and duplicate `--lang`
options fail without creating a file.

Each analyzer retains its default extensions because no `fileExtensions` override is
written (`.cs` for C#). Only built-in exclusions apply; submodules remain included.

Initialization never replaces an existing configuration, including a symbolic link or
directory at that path. It writes a temporary file in the target directory and publishes
the complete YAML without replacement, cleaning up on failure or cancellation.
Invalid arguments, languages and analyzer selections leave the target unchanged.

`init --help` and `init -h` show usage without accessing the repository. Exit codes are
0 for success/help, 1 for invalid input, existing configuration or filesystem errors,
and 130 for cancellation. Top-level `--help` lists both console commands.

## Example

```yaml
version: 1

analyzers:
  - dotnet-csharp
  # - typescript  # Planned, not yet implemented

# Optional: replaces the defaults for this analyzer; omit to keep .cs only.
fileExtensions:
  dotnet-csharp: [.cs, .csharp]

semanticIndexing:
  language: fr   # Optional; defaults to en

exclude:
  - submodules/**
  - third_party/
  - "**/*.g.cs"
```

- `version` must be `1`.
- `analyzers` is a required, non-empty list of installed analyzer IDs. Currently only `dotnet-csharp` is registered.
- `fileExtensions` is an optional mapping of enabled analyzer IDs to replacement extension lists.
- `semanticIndexing.language` is optional and defaults to `en` (English).
- `exclude` is optional. Rules add to, rather than replace, the built-in defaults.
- Git submodules are **not** excluded automatically; list their checkout paths if they should be skipped.
- Unknown YAML properties, duplicate keys, missing configuration, and unknown/unimplemented analyzers fail explicitly.

## Recursive discovery and file extensions

Discovery visits files directly at the repository root and recursively visits its nested
directories. It does not require a solution or project file and does not execute builds.
Files in each directory are returned in ordinal name order, followed by subdirectories in
ordinal depth-first order. Excluded subtrees and symbolic links are skipped.

Each analyzer supplies its own default extensions:

| Installed analyzer | Default extensions |
| --- | --- |
| `dotnet-csharp` | `.cs` |

An omitted `fileExtensions` section, an empty mapping (`{}`), or an omitted analyzer entry
keeps the corresponding analyzer's defaults. A configured list **replaces** those defaults:

```yaml
fileExtensions:
  dotnet-csharp: [.csharp]       # Only .csharp files; .cs files are no longer selected.
```

Use `[.cs, .csharp]` to retain `.cs` while adding a custom extension. Overrides select
which paths are sent to that analyzer; they do not add support for another programming
language or syntax. Custom extensions must contain source understood by the selected analyzer.
TypeScript discovery requires an implemented and enabled TypeScript adapter, which is not
available yet. An override cannot bypass analyzer availability checks.

- Extension comparison is case-insensitive; entries are trimmed, lowercased and deduplicated.
- Entries must start with `.` followed by one or more ASCII letters, digits, `_` or `-`.
- Only the final extension is matched: `.cs` includes `Customer.g.cs` but not `Customer.cs.bak`.
- Paths, glob patterns, compound suffixes such as `.g.cs`, empty lists, and null values
  are invalid. Use `exclude: ["**/*.g.cs"]` to omit generated files.
- Override keys must refer to enabled analyzers. IDs are normalized like the `analyzers` list;
  duplicate keys after normalization are rejected.
- If several enabled analyzers select the same extension, the first in the `analyzers`
  list is used, preserving the existing analyzer priority.
- All built-in and configured exclusions still apply to custom extensions.

The file walker owns traversal and exclusions; the application applies the resolved
extension selections. No source contents are read or copied during discovery.

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

## Running a read-only console preview

For local debugging without an MCP client, run from the CodeSemantic4Llm checkout:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  preview /absolute/path/to/checkout --max-files 200
```

The target checkout must contain `codesemantic4llm.yaml`. The command reuses the MCP
preview use case and prints effective analyzer extensions, exclusions, semantic language,
relative file paths, and whether more matching files exist. It does not read source
contents or execute code from the target checkout. The path is supplied directly by
the local operator; `CODESEMANTIC4LLM_REPOSITORY_ROOT` is only needed for MCP mode.

In Rider, select the `CodeSemantic4Llm.Mcp.Server` project in a **.NET Project** run
configuration and use `preview /absolute/path/to/checkout --max-files 200` as the
program arguments. Use an absolute path so the IDE's working directory does not
affect repository selection. Run shows the result in the console; Debug also allows
breakpoints in `PreviewRepositoryIndex.ExecuteAsync` and
`FileSystemRepositoryFileEnumerator.EnumerateFiles`.

Omit `--max-files` to use 50; limits must be between 1 and 200. `preview --help` shows
usage without accessing a repository. The command exits with 0 on success, 1 for
input/configuration/discovery errors, and 130 on cancellation.

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

The `get_repository_configuration` MCP tool returns the validated settings, including
`semanticIndexing.language` (default `en`), built-in exclusions, and the requested
`fileExtensions` overrides (an empty mapping when omitted). Analyzer defaults are resolved
by the application when previewing. The preview reports `effectiveFileExtensions` as a
mapping of analyzer IDs to extension arrays, alongside `semanticIndexingLanguage`.
The `preview_repository_index` MCP tool selects a **bounded** set of candidate source files
without reading their contents or writing a database. The default limit is 50, with a
maximum of 200. `hasMore` is true only when another matching, non-excluded file exists;
unmatched files do not consume the limit. If an unavailable analyzer is listed in the
YAML, the preview fails rather than silently ignoring its files.

`inspect_source` remains an explicit, in-memory snippet analysis tool and does not
use repository YAML settings.

## Executable specifications

Functional behavior is described in
`tests/CodeSemantic4Llm.FunctionalTests/Features/RepositoryConfiguration.feature` and
`tests/CodeSemantic4Llm.FunctionalTests/Features/RecursiveFileDiscovery.feature`, with console
behavior in `tests/CodeSemantic4Llm.FunctionalTests/Features/ConsoleRepositoryPreview.feature`
and `tests/CodeSemantic4Llm.FunctionalTests/Features/ConsoleRepositoryInitialization.feature`,
using [Reqnroll](https://reqnroll.net/) and xUnit v3. The tests create temporary
repositories, write YAML, create sample files, run the real loader and directory walker,
and assert which files would be indexed. They do not require Git, MCP clients or SQLite.

Run all tests with `dotnet test CodeSemantic4Llm.slnx`.
