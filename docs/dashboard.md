# Local index dashboard

**English** · [Français](dashboard.fr.md)

The `dashboard` console command launches a **read-only browser explorer** of all
partial and published indexes in the configured local SQLite cache. Unlike `serve`, it does
not require a repository argument, start MCP, or perform any indexing.

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- dashboard
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- dashboard --port 5091
```

Open **http://127.0.0.1:5080/** (or the selected port). Stop with Ctrl+C.
The default HTTP port is **5080**; the CLI accepts `--port` in the range 1–65535.
Only the IPv4 loopback interface is bound; there is no public network listener.

The explorer uses `CODESEMANTIC4LLM_INDEX_ROOT` if configured, otherwise the
same `LocalApplicationData/CodeSemantic4Llm/indexes` location used by `index`
and `serve`. Index each checkout and Git branch you want to examine first:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- index /path/to/checkout
```

The dashboard checks for updates every five seconds while visible. **Partial index** shows files and symbols as each file finishes. Stopping indexing preserves those files for browsing and search. References become available after verification and publication. MCP queries use the last published snapshot.

The declaration inspector displays stored XML documentation summaries and optional
LLM-generated summaries. Generation is controlled by `semanticIndexing.llm`; source
documentation is indexed even when generation is disabled. Summaries come from the saved SQLite
index; the dashboard never sends code to a model and never opens source files.
The server renders Markdown with [Markdig](https://github.com/xoofx/markdig): headings,
lists, emphasis, inline/fenced code, quotes and tables are formatted in the inspector.
Raw HTML is displayed as text, images retain only their labels, and only HTTP(S) and
mailto links remain clickable. Formatting is scoped to the summary panel. Rendering
uses the stored Markdown without changing it or requiring reindexing or an LLM call.

## What you can browse

- **Projects:** every recognized cache database, identified by the
  original checkout location. The display label is the final directory name;
  the YAML's optional `projectName` is not stored in SQLite.
- **Branches:** the latest visible generation of each Git branch, including
  recorded HEAD, completion time, semantic language and reference availability.
- **Overview:** counts for projects, source files, declarations, resolved usages,
  and a breakdown of declaration kinds.
- **Intent search:** natural-language queries over symbol names, qualified names
  and saved summaries, ranked with the same SQLite FTS5/BM25 implementation as MCP
  `search_code`. Choose a kind and a result limit (10 by default, up to 200).
  Results show their rank, location and Markdown summary; select a declaration
  to inspect its recorded usages. A notice identifies truncated results.
- **Symbols:** paginated search over qualified names and file paths, with
  filters for kind and source file. Select a symbol to inspect its exact
  declaration location, containing type and recorded usages.
- **Files:** search over indexed relative paths, including analyzer ID and
  version, file hash and declaration count. Select a file to see its symbols.
- **References:** paginated, semantically resolved relationships, with source
  and target locations. Legacy snapshots without reference indexing show no
  usages; reindex to enable them.

Navigation uses saved index data. The
recorded revision does not guarantee that files still match the snapshot.
Only metadata already stored in SQLite is displayed: the dashboard never reads
or copies source content. Searches are bounded to 200 records per request and
use SQL parameters. Missing or corrupt index databases are skipped during
discovery rather than initialized.

## Trying code search before connecting an LLM

Select a project and branch, open **Intent search**, and ask, for example,
`Where do we publish a branch snapshot?`. Submit with **Search code**. The results
identify the actual saved snapshot and recorded revision used for that query.
Changing the query, kind, limit, project or branch clears previous results;
submit again to evaluate the new selection.

This is lexical search, not embedding similarity or a query-time LLM call.
It uses the existing query normalization (English/French stop words, literal
prefix terms) and BM25 weights for name, qualified name and summary (8, 4, 1).
Without LLM generation, identifiers and available XML summaries remain searchable. Words in
the documentation's original language or the configured generation language usually work best; search does not translate
queries or infer synonyms. The displayed rank is an ordering, not a confidence score.

The dashboard resolves a validated cache ID and delegates to the same store
search as MCP, preserving ranking and limits without duplicating SQL. Unlike
the MCP use case, it searches the selected branch generation without checking
the checkout's current branch or HEAD. This permits offline inspection of saved
indexes; it does not promise that their locations match today's working tree.
Indexes predating FTS5 must be reindexed with `index`; searching never upgrades
or modifies them.

## Internal HTTP endpoints

All endpoints use GET and return JSON; the UI is embedded in the executable.

| Endpoint | Purpose |
| --- | --- |
| `/` | Embedded offline HTML/CSS/JavaScript interface |
| `/api/projects` | Valid cached projects with their indexed branches |
| `/api/summary?project=ID&branch=NAME` | Kind/language breakdown and reference count |
| `/api/files?project=ID&branch=NAME` | File metadata page |
| `/api/symbols?project=ID&branch=NAME` | Declaration page |
| `/api/search?project=ID&branch=NAME&q=QUESTION` | Ranked code intent matches |
| `/api/references?project=ID&branch=NAME` | Reference relationships page |

The paginated endpoints accept `q` (case-insensitive substring),
`offset` (default 0) and `limit` (default 50, maximum 200).
`/api/symbols` also accepts `kind` and `file`.
Each declaration retains its original `summary` and includes a transient `summaryHtml`
fragment for the inspector; absent or blank summaries have a null `summaryHtml`.
`/api/references` may be restricted to one declaration with
`targetPath`, `line` and `column`, which must be supplied together.

`/api/search` accepts a required `q` (up to 512 characters and at least one
searchable word), optional `kind`, and `limit` (default 10, range 1–200).
It returns `snapshot`, ranked `symbols` (including `summaryHtml`) and `hasMore`.
It has no offset or total count. Invalid queries return 400, unknown projects or
branches return 404, and indexes needing reindexing return 409. Request
cancellation propagates to the shared search implementation.

Project IDs are the validated, opaque cache directory hashes returned by
`/api/projects`; callers cannot substitute arbitrary file paths.
The process uses SQLite read-only connections and restricts HTTP to local
loopback without enabling CORS. No CDN, npm installation or network connection
is required for the UI. This dashboard is intended for a **trusted local
workstation**, not for reverse-proxying onto a network or exposing to the internet.
