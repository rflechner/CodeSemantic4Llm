# Install CodeSemantic4Llm as a .NET tool

**English** · [Français](dotnet-tool.fr.md)

CodeSemantic4Llm is a **command-line application and local stdio MCP server**. It is
distributed as a .NET Tool NuGet package, **`CodeSemantic4Llm.Mcp.Server`**, in the
`rflechner` GitHub Packages NuGet feed. The installed executable is named
**`codesemantic4llm`**.

This is a tool installation, **not** a `<PackageReference>` added to your application's
`.csproj`. For a project-local installation, commit a .NET tool manifest instead.

## Prerequisites

- The **.NET 10 SDK** (the tool targets `net10.0` and uses the ASP.NET Core shared runtime).
- Git and an existing Git checkout on a named branch for indexing.
- A GitHub account and a **personal access token (classic)** with `read:packages`.

## 1. Configure the NuGet feed (once per developer)

Register the feed in your user-level NuGet configuration (skip this if it already exists):

```sh
dotnet nuget add source "https://nuget.pkg.github.com/rflechner/index.json" --name CodeSemanticGitHub
```

Use a personal access token (classic) with `read:packages`, required even for
public packages. We recommend supplying it through an environment variable
rather than storing it in a configuration file. For example, in PowerShell 7:

```powershell
$packagesPat = Read-Host "GitHub PAT (classic, read:packages)" -MaskInput
$env:NuGetPackageSourceCredentials_CodeSemanticGitHub = "Username=YOUR_GITHUB_LOGIN;Password=$packagesPat;ValidAuthenticationTypes=Basic"
Remove-Variable packagesPat
```

Replace `YOUR_GITHUB_LOGIN` with the token owner's GitHub login.
NuGet automatically looks for `NuGetPackageSourceCredentials_{sourceName}`:
`CodeSemanticGitHub` matches the name given to `add source`, not the feed URL.
You create the variable; NuGet reads it. Match the source name's casing,
particularly on Linux. See [NuGet's environment credential convention](https://learn.microsoft.com/en-us/nuget/consume-packages/consuming-packages-authenticated-feeds#credentials-in-environment-variables).

Run the installation, update or restore in the same terminal session; set the
variable again in future sessions. Keep credentials out of source control.
For token creation and other authentication options, follow
[GitHub's NuGet authentication guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages).

## 2. Install the tool

### Recommended: local to a Git repository

From the root of **your own** repository:

```sh
dotnet new tool-manifest
dotnet tool install CodeSemantic4Llm.Mcp.Server --local
dotnet codesemantic4llm --help
```

If a `.config/dotnet-tools.json` already exists, skip `dotnet new tool-manifest`.
Commit `.config/dotnet-tools.json` so other contributors use the same package
version. Each contributor authenticates to the feed as described above, then runs
`dotnet tool restore`. Do **not** add `CodeSemantic4Llm.Mcp.Server` as a
`PackageReference` to your application.

### Alternative: global for your user account

```sh
dotnet tool install --global CodeSemantic4Llm.Mcp.Server
codesemantic4llm --help
```

The global command is `codesemantic4llm` (not `dotnet run`). On Windows
its shim is in `%USERPROFILE%\.dotnet\tools`; on Linux/macOS it is in
`$HOME/.dotnet/tools`. Ensure that directory is on the MCP client's `PATH`,
or use an absolute path to the shim in the MCP JSON.

To upgrade later:

```sh
dotnet tool update CodeSemantic4Llm.Mcp.Server --local
# Or for a global installation:
dotnet tool update CodeSemantic4Llm.Mcp.Server --global
```

## 3. Initialize and index your repository

Examples below use the **local** installation from the repository root.
For global installations, replace `dotnet codesemantic4llm` with `codesemantic4llm`.

```sh
dotnet codesemantic4llm init . --name "My Repository" --analyzer dotnet-csharp
dotnet codesemantic4llm preview . --max-files 50
dotnet codesemantic4llm index .
```

`init` creates `codesemantic4llm.yaml` at the checkout root. Skip it if you
already have a YAML file. Currently `dotnet-csharp` is the supported analyzer;
`semanticIndexing.language` defaults to `en`. Indexing persists derived
metadata in a branch-aware SQLite cache, **not** copies of source files.

Re-run `index` after source changes, a branch switch, or an MCP-assisted edit.
The MCP server itself does **not** rebuild the index on startup.

For a custom cache path, set `CODESEMANTIC4LLM_INDEX_ROOT` to the **same
absolute path** when invoking `index`, `dashboard`, and your MCP client.
For the configuration schema and indexing semantics, see
[repository configuration](repository-configuration.md) and
[indexing](indexing.md).

## 4. Configure an MCP client

The MCP transport is **stdio**, and the executable is started with
`serve <repository-path>`. Do not start it via `dotnet run` in an MCP client;
build output can corrupt the stdio protocol. The examples below start in
**read-only mode**.

### VS Code / GitHub Copilot Chat (local tool)

Save as **`.vscode/mcp.json`** in the repository that contains your
`.config/dotnet-tools.json` manifest:

```json
{
  "servers": {
    "codeSemantic4Llm": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["codesemantic4llm", "serve", "${workspaceFolder}"],
      "cwd": "${workspaceFolder}"
    }
  }
}
```

For a **global** installation instead, use `"command": "codesemantic4llm"`
and `"args": ["serve", "${workspaceFolder}"]`, omitting `cwd` if desired.

When using a custom index cache, add an `env` field (within the server object):

```json
"env": {
  "CODESEMANTIC4LLM_INDEX_ROOT": "/absolute/path/to/indexes"
}
```

This last snippet is a field to add to the complete server object above,
not a standalone `mcp.json` document. Restart the MCP server in VS Code
after changing the JSON and accept its trust prompt.

### GitHub Copilot CLI

Save the following as **`.github/mcp.json`** in your repository or use
`~/.copilot/mcp-config.json` for a personal configuration:

```json
{
  "mcpServers": {
    "codeSemantic4Llm": {
      "type": "local",
      "command": "dotnet",
      "args": ["codesemantic4llm", "serve", "."],
      "tools": ["*"]
    }
  }
}
```

Launch `copilot` **from the repository root**, so `.` identifies the checkout
and `dotnet` finds its local tool manifest. For a global tool, change the
command to `codesemantic4llm` and arguments to `["serve", "."]`.
Copilot CLI uses **`mcpServers`** and `type: "local"`; VS Code's
`.vscode/mcp.json` instead uses **`servers`** and `type: "stdio"`.

The per-repository file may be committed if your team trusts the tool;
Copilot CLI asks for repository trust before loading project MCP servers.

### Claude Desktop / generic `mcpServers` clients (global tool)

Clients that accept a generic `mcpServers` object can use:

```json
{
  "mcpServers": {
    "codeSemantic4Llm": {
      "command": "codesemantic4llm",
      "args": ["serve", "/absolute/path/to/your/repository"]
    }
  }
}
```

Replace the absolute path with your real checkout path. If the desktop app
cannot find the global executable, specify its full path in `command`
(e.g. `C:\\Users\\YOUR_USER\\.dotnet\\tools\\codesemantic4llm.exe` on Windows).
The exact config file location varies by MCP client.

### Optional MCP server switches

The server is **read-only by default**. To explicitly expose source editing,
append `--allow-write` to `args`. Editing tools are only registered in that
mode. You can also append `--expose-indexing` to advertise operator-facing
index/status tools, or `--log-file /absolute/path/to/log.jsonl` to log
requests and responses. File logs can contain **source code and sensitive
parameters**; store them privately. Restart the client after changing switches.

## 5. Verify that the integration works

1. Verify `dotnet codesemantic4llm --help` (or the global executable) starts.
2. Verify `codesemantic4llm.yaml` exists and `index` has completed for the
   current Git branch.
3. Restart the MCP client and verify that `get_server_info` and
   `find_symbols` appear in the available tools.
4. Try asking the agent to find a class by name. The agent should call
   `find_symbols` rather than read the whole repository.
5. Optionally run `dotnet codesemantic4llm dashboard --port 5080` and open
   `http://127.0.0.1:5080/` to inspect the locally cached index.

### Troubleshooting

- **401/403 while installing**: check your feed credentials and package access
  using [GitHub's authentication guide](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-to-github-packages).
- **The server is not listed**: check your MCP client's distinct JSON schema,
  executable `PATH`, local tool manifest and working directory. Running
  `serve` manually will wait for an MCP client to talk over stdio.
- **No results**: run `index` on the current branch, and check the repository
  path and optional `CODESEMANTIC4LLM_INDEX_ROOT`. The cache is branch-aware.
- **Other NuGet restores fail after adding the source**: NuGet may query all
  feeds. Use package-source mapping in your own NuGet configuration or remove
  the source when not needed with `dotnet nuget remove source CodeSemanticGitHub`.

## Maintainer guide to staging and promotion

Run the same NUKE build as CI from the repository root:

```sh
dotnet run --project build/Build.csproj -- Pack
```

NUKE restores the solution, builds in Release, runs the complete test suite and
packs the server into `artifacts/`. The base version comes from `<Version>` in
`src/CodeSemantic4Llm.Mcp.Server/CodeSemantic4Llm.Mcp.Server.csproj`.
The [CI workflow](../.github/workflows/ci.yml) installs the package and checks
`--help`, `init` and `preview` before uploading it as a 30-day artifact.

| CI event | Package version | Destination |
| --- | --- | --- |
| Pull request | `<base>-alpha-<commit>` | Actions artifact |
| Push to `develop` | `<base>-beta-<commit>` | Actions artifact and GitHub Packages |
| Push to `main` | `<base>` | Actions artifact and GitHub Packages |

The suffix uses the first eight characters of the commit SHA. Choose a new
base version before merging to `main` if the previous stable version is already
staged. GitHub Packages versions are immutable.

To enable public releases, create the GitHub environment `nuget-org` and
configure a GitHub Actions trusted publishing policy on NuGet.org for this
repository, the workflow filename `publish-tool.yml` and that environment.
Set the environment secret `NUGET_USER` to the NuGet.org account name.
You can configure required reviewers on the environment to gate promotion.

To release, open **Promote .NET tool to NuGet.org** in Actions. Under **Use workflow from**,
select `develop` for a beta (`X.Y.Z-beta-<8-character commit SHA>`) or `main` for
a stable version (`X.Y.Z`). Enter the exact staged version. For a stable version, selection defaults
to the latest successful CI push on `main`. For a beta, the version identifies
the commit on `develop`. Supply the CI run ID to select an earlier stable build.
The workflow checks the run, package ID, version and source commit, then pushes
the existing package to NuGet.org using a short-lived credential. If the
artifact expired, it retrieves the staged version from GitHub Packages.

**First publication:** GitHub initially creates a **private package**.
If this tool should be installable by everyone, visit your GitHub
account's Packages settings, select `CodeSemantic4Llm.Mcp.Server`,
and change **package visibility** to Public. The repository can remain
private; these are separate visibility controls when granular package
permissions are enabled. Making a package public exposes its compiled
contents, and changing a public package back to private may not be
supported. Users still need a classic PAT to install from GitHub's
NuGet registry.

References: [GitHub NuGet authentication](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry),
[VS Code MCP](https://code.visualstudio.com/docs/agents/reference/mcp-configuration),
[Copilot CLI MCP](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers).

## Shared summary commands

The installed tool also provides `cache-export <repository-path> [--overwrite]`,
`watch <repository-path>` and `setup-git <repository-path>`. The latter installs a
local driver invoking `git-merge-summary <current|other> <ancestor> <current> <other>`.
See the [shared summary guide](shared-summaries.md) before selecting a Git conflict
policy, especially for rebases and uncommitted Markdown.
