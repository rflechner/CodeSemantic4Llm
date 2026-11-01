using System.Diagnostics;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises opt-in logging with real stdio calls while the log is still open.</summary>
public sealed partial class ServeRepositorySteps
{
    private const string SourceLogMarker = "SOURCE_PAYLOAD_238d8f";
    private string _fileLogText = "";
    private string _fileLogConsoleText = "";
    private readonly Dictionary<int, JsonElement> _loggedMcpParameters = [];
    private readonly Dictionary<int, JsonElement> _loggedMcpReplies = [];
    private string ServerLogPath => Path.Combine(_workspace, "logs", "server.jsonl");

    [When(@"I make failing and successful MCP calls with ""(.*)"" file logging")]
    public async Task WhenCallMcpWithFileLogging(string mode)
    {
        var arguments = new List<string> { "serve", SelectedRoot, "--log-file", "logs/server.jsonl" };
        if (mode == "detailed")
        {
            arguments.Add("--expose-indexing");
        }

        using var process = Process.Start(CreateStartInfo(arguments.ToArray(), null))!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await SendAsync(process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"file-log-test","version":"1.0"}}}
                """, timeout.Token);
            await ReceiveAsync(process, 1, timeout.Token);
            await SendAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", timeout.Token);

            var success = await CallLoggedToolAsync(process, 2, "get_server_info", new { }, timeout.Token);
            Assert.False(success.TryGetProperty("isError", out var error) && error.GetBoolean());
            var missing = await CallLoggedToolAsync(process, 3, "get_file_diagnostics",
                new { relativePath = "Missing.cs" }, timeout.Token);
            Assert.True(missing.GetProperty("isError").GetBoolean());

            var invalid = await CallLoggedToolAsync(process, 4, "validate_method_body",
                ValidationArguments(mode), timeout.Token);
            Assert.True(invalid.GetProperty("isError").GetBoolean());
            await CallLoggedToolAsync(process, 5, "get_server_info", null, timeout.Token);
            await CallLoggedToolAsync(process, 6, "unknown_tool_for_logging",
                new { nested = new { items = new object?[] { "line one\nline two — été", 7, true, null } } },
                timeout.Token);

            // No shutdown is needed to make the most recent failure available to the operator.
            _fileLogText = SharedLogFile.ReadAllText(ServerLogPath);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            _fileLogConsoleText = await errors;
        }
    }

    [Then("the file log identifies the calls and their exceptions")]
    public void ThenFileLogContainsFailures()
    {
        var entries = _fileLogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(ParseLogLine).ToArray();
        Assert.All(entries, entry => Assert.True(entry.GetProperty("processId").GetInt32() > 0));
        var calls = entries.Where(entry => entry.GetProperty("requestId").GetString() is not null).ToArray();
        Assert.Contains(calls, entry => entry.GetProperty("requestId").GetString() == "2"
            && entry.GetProperty("toolName").GetString() == "get_server_info"
            && entry.GetProperty("message").GetString()!.Contains("completed"));
        Assert.Contains(calls, entry => entry.GetProperty("requestId").GetString() == "3"
            && entry.GetProperty("toolName").GetString() == "get_file_diagnostics"
            && entry.GetProperty("exception").GetString() is { } exception
            && exception.Contains("FileNotFoundException") && exception.Contains("RepositorySourcePath"));
        Assert.Contains(calls, entry => entry.GetProperty("requestId").GetString() == "4"
            && entry.GetProperty("toolName").GetString() == "validate_method_body"
            && entry.GetProperty("exception").GetString() is not null);
        Assert.Contains(calls, entry => entry.GetProperty("requestId").GetString() == "5"
            && entry.GetProperty("message").GetString()!.Contains("completed"));
    }

    [Then("the file log contains the exact parameters and replies for each call")]
    public void ThenFileLogContainsPayloads()
    {
        var entries = _fileLogText.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(ParseLogLine).ToArray();

        foreach (var (id, parameters) in _loggedMcpParameters)
        {
            var calls = entries.Where(entry => entry.GetProperty("requestId").GetString() == id.ToString()).ToArray();
            var request = Assert.Single(calls, entry => entry.GetProperty("parameters").ValueKind != JsonValueKind.Null);
            var response = Assert.Single(calls, entry => entry.GetProperty("response").ValueKind != JsonValueKind.Null);
            Assert.Equal(parameters.GetProperty("name").GetString(), request.GetProperty("toolName").GetString());
            Assert.Equal(request.GetProperty("toolName").GetString(), response.GetProperty("toolName").GetString());
            Assert.True(JsonElement.DeepEquals(parameters, request.GetProperty("parameters")), request.ToString());
            Assert.True(JsonElement.DeepEquals(_loggedMcpReplies[id], response.GetProperty("response")), response.ToString());
        }

        Assert.Contains(SourceLogMarker, _fileLogText);
    }

    [Then("tool payloads are absent from console logs")]
    public void ThenNoPayloadsInConsoleLog() => Assert.DoesNotContain(SourceLogMarker, _fileLogConsoleText);

    [Then("MCP responses contain no logging configuration")]
    public void ThenNoLogSettingsInReplies() => Assert.All(_loggedMcpReplies.Values, reply =>
    {
        Assert.DoesNotContain("log-file", reply.GetRawText());
        Assert.DoesNotContain("server.jsonl", reply.GetRawText());
        Assert.DoesNotContain("stackTrace", reply.GetRawText());
    });

    [Then("no server log file was created")]
    public void ThenNoLogFile() => Assert.False(Directory.Exists(Path.GetDirectoryName(ServerLogPath)));

    private async Task<JsonElement> CallLoggedToolAsync(
        Process process, int id, string name, object? arguments, CancellationToken cancellationToken)
    {
        var parameters = arguments is null
            ? JsonSerializer.SerializeToElement(new { name })
            : JsonSerializer.SerializeToElement(new { name, arguments });
        _loggedMcpParameters.Add(id, parameters);
        await SendAsync(process, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call", @params = parameters
        }), cancellationToken);
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        Assert.NotNull(line);
        var response = ParseLogLine(line);
        Assert.Equal(id, response.GetProperty("id").GetInt32());
        _loggedMcpReplies.Add(id, response);
        return response.TryGetProperty("result", out var result) ? result : response;
    }

    private static object ValidationArguments(string mode)
    {
        var body = $"{{\n    var value = \"{SourceLogMarker} — été\";\n}}";
        return mode == "detailed"
            ? new { relativePath = "Missing.cs", line = 1, column = 1, snapshotId = 1,
                expectedContentHash = "unused", newBody = body }
            : (object)new { relativePath = "Missing.cs", name = "Run", newBody = body };
    }

    private static JsonElement ParseLogLine(string line)
    {
        using var entry = JsonDocument.Parse(line);
        return entry.RootElement.Clone();
    }
}
