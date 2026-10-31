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
  init /absolute-or-relative/path/to/repository --name "My Project" --analyzer dotnet-csharp --lang fr-CH
```

`init <repository-path> [--name <project-name>] [--lang <language-tag>] [--analyzer <analyzer-id>]...`
creates `codesemantic4llm.yaml` directly in the specified existing directory.
Absolute paths and paths relative to the process's
working directory are accepted, including `.` and `..`; quote paths containing spaces.
The command does not create missing directories or require Git metadata, and does not
start an MCP session, discover source files, or build an index.

When all options are omitted, the generated defaults are:

```yaml
version: 1
analyzers:
  - dotnet-csharp
semanticIndexing:
  language: en
  llm:
    enabled: false
exclude: []
```

`--name "My Project"` writes `projectName: My Project`. The optional name is a display
label, independent of the checkout path, branch identity and cache location. It is trimmed
at both ends; case, spaces inside the name and Unicode characters are preserved. Empty or
whitespace-only names, control characters and line breaks are rejected. Quote a name
containing spaces when passing it through a shell. YAML-sensitive characters are escaped
by the writer, so names such as `Payments: API #1` round-trip correctly.

Without `--name`, no `projectName` key is written. Existing YAML without this property
remains valid; omission or YAML `null` means there is no configured display name. The name
is returned by `get_repository_configuration` and `preview_repository_index`, and shown
by the console preview when present. Changing only this label does not invalidate reusable
declarations or create a different index cache.

`--lang` selects `semanticIndexing.language`, using the same validation and case
normalization as the YAML loader (for example, `FR-ch` becomes `fr-CH`). Its default
is `en`.

Repeat `--analyzer <analyzer-id>` to select multiple analyzers. An explicit selection
replaces the default `dotnet-csharp` selection. IDs are trimmed, lowercased and
deduplicated, retaining the order of their first occurrence. Every selected analyzer
must be available before any configuration is written; unknown or unimplemented IDs
fail with the available analyzer list. Currently only `dotnet-csharp` is installed;
`typescript` remains planned and cannot be selected yet.

Options may appear before or after the repository path, including `--lang` between
two `--analyzer` options. Spectre.Console.Cli also accepts `--name=MyProject`, `--lang=fr`
and `--analyzer=dotnet-csharp`. Each occurrence requires one value; only `--analyzer` is
repeatable. Empty names or analyzer IDs, missing values, unknown options, and duplicate
`--name` or `--lang` options fail without creating a file.

Each analyzer retains its default extensions because no `fileExtensions` override is
written (`.cs` for C#). Only built-in exclusions apply; submodules remain included.

Initialization never replaces an existing configuration, including a symbolic link or
directory at that path. It writes a temporary file in the target directory and publishes
the complete YAML without replacement, cleaning up on failure or cancellation.
Invalid arguments, project names, languages and analyzer selections leave the target unchanged.

`init --help` and `init -h` show usage without accessing the repository. Exit codes are
0 for success/help, 1 for invalid input, existing configuration or filesystem errors,
and 130 for cancellation. Top-level `--help` lists `init`, `preview`, `index`, and `serve`.
Spectre.Console generates help from the command settings. Results and help use stdout;
parsing, validation and operation errors use stderr. Unknown commands and options fail
instead of starting the MCP server. `serve [repository-path]`, or an invocation without
arguments, starts the stdio MCP server with logs confined to stderr. The optional path
overrides `CODESEMANTIC4LLM_REPOSITORY_ROOT` for this server instance.

## Example

```yaml
version: 1
projectName: My Project

analyzers:
  - dotnet-csharp
  # - typescript  # Planned, not yet implemented

# Optional: replaces the defaults for this analyzer; omit to keep .cs only.
fileExtensions:
  dotnet-csharp: [.cs, .csharp]

semanticIndexing:
  language: fr   # Optional; defaults to en
  llm:
    enabled: true
    baseUrl: http://127.0.0.1:1234/v1
    model: my-local-model
    # apiKey: your-api-key
    # apiKeyEnvironmentVariable: CODESEMANTIC4LLM_LLM_API_KEY

exclude:
  - submodules/**
  - third_party/
  - "**/*.g.cs"
```

- `version` must be `1`.
- `projectName` is an optional, non-empty, single-line display name; omission or null leaves it unspecified.
- `analyzers` is a required, non-empty list of installed analyzer IDs. Currently only `dotnet-csharp` is registered.
- `fileExtensions` is an optional mapping of enabled analyzer IDs to replacement extension lists.
- `semanticIndexing.language` is optional and defaults to `en` (English).
- `semanticIndexing.llm.enabled` is optional and defaults to `false` (no LLM calls).
- When enabled, `semanticIndexing.llm.baseUrl` (HTTP(S) OpenAI-compatible API root)
  and `semanticIndexing.llm.model` are required. `apiKey` and `apiKeyEnvironmentVariable` are optional.
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
**LLM-generated summaries and future natural-language index text**
are written. It is separate from `analyzers`, which choose the programming languages
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
The code is validated syntactically; actual language support depends on the selected model.
Changing the language invalidates previously generated descriptions through the index
configuration fingerprint.

## Optional per-repository LLM summaries

Set `semanticIndexing.llm.enabled: true` in each checkout's YAML to opt in.
Only this repository sends snippets to the configured LLM endpoint. Without this option
(or when false), indexing remains fully offline and does not call an LLM.

- `baseUrl` is an OpenAI-compatible API root, such as `http://127.0.0.1:1234/v1`
  for LM Studio. CodeSemantic4Llm posts to `{baseUrl}/chat/completions`.
- `model` is sent in the `model` field. Use the model identifier advertised by your server.
- `apiKey` supplies an optional Bearer token directly in YAML. Surrounding whitespace
  is removed; an empty or whitespace-only value is treated as absent. A non-empty value
  takes priority over `apiKeyEnvironmentVariable`, even if that variable is unset.
- `apiKeyEnvironmentVariable` names a fallback environment variable containing a Bearer
  token. It is read only when `apiKey` is absent. Set it in the environment of the process
  running `index`; a missing or blank fallback value fails the request. If neither option
  is configured, requests omit the Authorization header.
- Inline keys are stored as plain text in repository YAML; keep real keys out of version
  control. MCP configuration responses omit `apiKey`, including their payload logs.
  Keys are not stored in SQLite, and rotating an inline key does not regenerate summaries.
- The base URL supports only HTTP(S), without embedded credentials or query strings.
  The API must implement OpenAI-compatible chat completions; vendor-specific endpoints
  requiring different paths or authentication need a compatible gateway.

The indexer sends the **whole C# method**, including its preceding XML documentation
comments (`summary`, `param`, `returns`, etc.), or the **whole class/type
declaration**, including its members, to the selected model. The prompt requests an
accurate, concise summary in `semanticIndexing.language`. Stored summaries are limited
to **10 non-empty lines per declaration**. They are saved with the symbol in SQLite,
returned by MCP symbol queries and shown in the dashboard declaration inspector.
Source snippets and prompt bodies are never saved in the index cache.

LLM summarization is opt-in because sending source to any HTTP endpoint can expose
proprietary code. Use a trusted local endpoint or an explicitly authorized service.
An unresponsive or failed LLM request aborts the new index generation: the previous
published snapshot remains readable. Changing the model, URL, language, enable flag,
or source content invalidates the appropriate reuse. Unchanged files preserve their
summaries; no vector embeddings are generated yet.

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
the local operator; no MCP environment configuration is required.

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
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- serve /absolute/path/to/checkout
```

For PowerShell:

```powershell
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- serve "D:\source\my-project"
```

`serve [repository-path]` accepts an existing directory, absolute or relative to the
process working directory. Use an absolute path in client configuration when the working
directory is uncertain. A supplied path takes precedence over
`CODESEMANTIC4LLM_REPOSITORY_ROOT`; `serve` without a path and startup without any arguments
retain environment-based selection. An empty or nonexistent argument fails before the
server starts. `serve --help` describes the optional argument without starting MCP.

Startup does not require Git metadata or YAML to initialize the MCP protocol, and does
not index automatically. Repository tools validate the YAML and Git state when needed.
Without either selection method, repository tools report a configuration error while
repository-independent tools remain available. See the [mcp.json example](../README.md#mcp-client-configuration).

The `get_repository_configuration` MCP tool returns the validated settings, including
`projectName` (null when unspecified), `semanticIndexing.language` (default `en`), built-in exclusions, and the requested
`fileExtensions` overrides (an empty mapping when omitted). Analyzer defaults are resolved
by the application when previewing. The preview reports `effectiveFileExtensions` as a
mapping of analyzer IDs to extension arrays, alongside `semanticIndexingLanguage` and `projectName`.
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
and command routing in `tests/CodeSemantic4Llm.FunctionalTests/Features/ConsoleCommandRouting.feature`.
`tests/CodeSemantic4Llm.FunctionalTests/Features/ServeRepository.feature` covers absolute and
relative serve paths, environment precedence and MCP preview calls through a real server,
using [Reqnroll](https://reqnroll.net/) and xUnit v3. The tests create temporary
repositories, write YAML, create sample files, run the real loader and directory walker,
and assert which files would be indexed. The routing scenarios also launch the built
server and send an MCP initialization request to verify that stdout remains protocol-only.
Those preview scenarios do not require Git or SQLite. Persistent indexing scenarios in
`tests/CodeSemantic4Llm.FunctionalTests/Features/RepositoryIndexing.feature` use real temporary
Git checkouts and SQLite to verify full indexing, reuse, stale-record removal, rollback,
branch isolation and saved-index queries. See [the indexing guide](indexing.md).

Run all tests with `dotnet test CodeSemantic4Llm.slnx`.
