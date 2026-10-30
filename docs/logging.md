# MCP file logging and troubleshooting

Enable file logging when starting the server:

```sh
dotnet run --project src/CodeSemantic4Llm.Mcp.Server -- \
  serve /path/to/repository --log-file /path/to/logs/codesemantic4llm.jsonl
```

For an MCP client, append the option to the arguments used to launch the built DLL:

```json
{
  "command": "dotnet",
  "args": [
    "/absolute/path/to/CodeSemantic4Llm.Mcp.Server.dll",
    "serve",
    "/absolute/path/to/repository",
    "--log-file",
    "/absolute/path/to/logs/codesemantic4llm.jsonl"
  ]
}
```

Restart the MCP server after changing its arguments. File logging is disabled when
`--log-file` is omitted. It is independent of `--expose-indexing` and `--allow-write`,
and is not exposed as a tool or a model-facing parameter. Console logs continue to
go to stderr; stdout contains only MCP protocol messages.

Relative log paths resolve from the server process's working directory, not the
selected repository root. Missing parent directories are created. An absolute path
outside the source checkout is convenient when clients use different working directories.
If the file cannot be opened, startup fails with an error on stderr rather than
silently losing logs. Use a separate log path for each concurrently running server.

## Reading a failure

The file contains UTF-8 JSON lines, one record per event, flushed immediately so it
can be read while the server is running:

```sh
tail -f /path/to/logs/codesemantic4llm.jsonl
```

Each record includes:

- `timestamp`: UTC time in ISO 8601 format.
- `processId`, `category`, `level`, and `eventId`: which process and logger produced it.
- `requestId` and `toolName`: correlation fields for logs within a tool call.
- `message`: the call start, outcome and elapsed milliseconds, or application details.
- `exception`: exception type, message, stack trace and inner exceptions when present.
- `parameters`: the complete `tools/call` parameters object, including `name` and
  `arguments`, on the received-parameters record.
- `response`: the complete outgoing JSON-RPC reply, including `id` and `result` or
  `error`, on the response record. Tool results retain `content`, `structuredContent`
  and `isError` when present. This includes error replies produced by the MCP SDK.

Payload fields are JSON objects, not JSON encoded inside strings, and are `null` on
other records. Match parameters, outcomes, exceptions and responses by `requestId`
and `processId`. Parameters preserve what the client supplied; omitted optional
arguments are not replaced with server defaults. Responses are recorded just before
transport sending, so a record does not prove the client received the reply.

Find the failing tool's `requestId`, then read all records with that ID and process ID.
The application may log the original technical failure before returning a simpler
message to the model. A failed call does not stop later requests from being logged.
Calls that return an MCP error result are also recorded as failures. Start records
without a corresponding outcome can help identify an interrupted or hung call.

Enabling `--log-file` records complete tool parameters and responses without
redaction or truncation, including supplied or returned source snippets. These
payload records go only to the selected file, not the console logger. Other MCP
messages, such as initialization and notifications, are not captured as payloads.
The file logger accepts Information and higher levels, with SDK transport logs
restricted to Warning and higher. Logs can therefore contain source code and any
sensitive values present in tool arguments, results or exception messages; review
them before sharing a log. The log is diagnostic output, separate from index storage.

## Retention and failures

The existing log is appended to across restarts. Before a new record would take the
file past 10 MiB, it is rotated to `.1`; the previous archives move to `.2` and `.3`.
Only the current file and three archives are retained. A single unusually large record
can exceed the threshold, because records are not split or truncated.

Each write is flushed to the operating system, not forced to physical storage; this
does not guarantee survival of a power loss. If writing or rotation fails while the
server runs, file logging is disabled and the failure is reported once on stderr.
Code operations and their MCP responses continue normally. Correct the destination
and restart the server to resume file logging.
