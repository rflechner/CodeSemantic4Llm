# Local index dashboard

The `dashboard` console command launches a **read-only browser explorer** of all
published indexes in the configured local SQLite cache. Unlike `serve`, it does
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

The declaration inspector displays generated LLM summaries when the repository opted
in to `semanticIndexing.llm.enabled`. Summaries come from the published SQLite
index; the dashboard never sends code to a model and never opens source files.

## What you can browse

- **Projects:** every recognized, published cache database, identified by the
  original checkout location. The display label is the final directory name;
  the YAML's optional `projectName` is not stored in SQLite.
- **Branches:** independently published Git branch snapshots, including
  recorded HEAD, completion time, semantic language and reference availability.
- **Overview:** counts for projects, source files, declarations, resolved usages,
  and a breakdown of declaration kinds.
- **Symbols:** paginated search over qualified names and file paths, with
  filters for kind and source file. Select a symbol to inspect its exact
  declaration location, containing type and recorded usages.
- **Files:** search over indexed relative paths, including analyzer ID and
  version, file hash and declaration count. Select a file to see its symbols.
- **References:** paginated, semantically resolved relationships, with source
  and target locations. Legacy snapshots without reference indexing show no
  usages; reindex to enable them.

Navigation uses **published snapshots**, not the current working tree. The
recorded revision does not guarantee that files still match the snapshot.
Only metadata already stored in SQLite is displayed: the dashboard never reads
or copies source content. Searches are bounded to 200 records per request and
use SQL parameters. Missing or corrupt index databases are skipped during
discovery rather than initialized.

## Internal HTTP endpoints

All endpoints use GET and return JSON; the UI is embedded in the executable.

| Endpoint | Purpose |
| --- | --- |
| `/` | Embedded offline HTML/CSS/JavaScript interface |
| `/api/projects` | Valid cached projects with their published branches |
| `/api/summary?project=ID&branch=NAME` | Kind/language breakdown and reference count |
| `/api/files?project=ID&branch=NAME` | File metadata page |
| `/api/symbols?project=ID&branch=NAME` | Declaration page |
| `/api/references?project=ID&branch=NAME` | Reference relationships page |

The paginated endpoints accept `q` (case-insensitive substring),
`offset` (default 0) and `limit` (default 50, maximum 200).
`/api/symbols` also accepts `kind` and `file`.
`/api/references` may be restricted to one declaration with
`targetPath`, `line` and `column`, which must be supplied together.

Project IDs are the validated, opaque cache directory hashes returned by
`/api/projects`; callers cannot substitute arbitrary file paths.
The process uses SQLite read-only connections and restricts HTTP to local
loopback without enabling CORS. No CDN, npm installation or network connection
is required for the UI. This dashboard is intended for a **trusted local
workstation**, not for reverse-proxying onto a network or exposing to the internet.
