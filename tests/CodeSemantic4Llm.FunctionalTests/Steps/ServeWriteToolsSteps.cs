using System.Diagnostics;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Confirms tool discovery follows the operator's process-startup permission.</summary>
public sealed partial class ServeRepositorySteps
{
    private string[] _advertisedTools = [];
    private JsonElement _toolDefinitions;
    private string _serverInformation = "";

    [When(@"I list MCP tools with ""(.*)"" server startup")]
    public async Task WhenListMcpTools(string mode)
    {
        string[] arguments = mode switch
        {
            "write-enabled" => ["serve", SelectedRoot, "--allow-write"],
            "read-only" => ["serve", SelectedRoot],
            "indexing" => ["serve", SelectedRoot, "--expose-indexing"],
            "indexing-write" => ["serve", SelectedRoot, "--expose-indexing", "--allow-write"],
            "default" => [],
            "unconfigured" => [],
            _ => throw new ArgumentException($"Unknown mode '{mode}'.")
        };

        var startInfo = CreateStartInfo(arguments, mode == "default" ? SelectedRoot : null);
        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await SendAsync(process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"write-tools-test","version":"1.0"}}}
                """, timeout.Token);
            var initialization = await ReceiveAsync(process, 1, timeout.Token);
            Assert.True(initialization.TryGetProperty("serverInfo", out _));

            await SendAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", timeout.Token);
            await SendAsync(process, """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""", timeout.Token);

            var listed = await ReceiveAsync(process, 2, timeout.Token);
            _toolDefinitions = listed.Clone();
            _advertisedTools = listed.GetProperty("tools").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();

            await SendAsync(process,
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_server_info","arguments":{}}}""",
                timeout.Token);
            var information = await ReceiveAsync(process, 3, timeout.Token);
            _serverInformation = information.GetProperty("content")[0].GetProperty("text").GetString()!;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }

    [Then("MCP tool descriptions do not claim a selected project")]
    public void ThenNoSelectedProject() => Assert.All(_toolDefinitions.GetProperty("tools").EnumerateArray(),
        tool => Assert.DoesNotContain("In project ", tool.GetProperty("description").GetString()));

    [Then("MCP advertises the method body reader")]
    public void ThenMethodReaderAdvertised()
    {
        Assert.Contains("get_method_body", _advertisedTools);
        Assert.Contains("get_method", _advertisedTools);
        Assert.Contains("validate_method_body", _advertisedTools);
        Assert.Contains("get_method_diagnostics", _advertisedTools);
        Assert.Contains("get_file_diagnostics", _advertisedTools);
        Assert.Contains("get_type_diagnostics", _advertisedTools);
        Assert.Contains("find_symbols", _advertisedTools);
        Assert.Contains("get_server_info", _advertisedTools);
    }

    [Then(@"MCP writing tools are ""(.*)""")]
    public void ThenWriteToolsVisibility(string state)
    {
        if (state == "available")
        {
            Assert.Contains("replace_method_body", _advertisedTools);
            Assert.Contains("replace_method", _advertisedTools);
            AssertMethodEditContracts();
        }
        else
        {
            Assert.Equal("absent", state);
            Assert.DoesNotContain("replace_method_body", _advertisedTools);
            Assert.DoesNotContain("replace_method", _advertisedTools);
        }
    }

    private void AssertMethodEditContracts()
    {
        var tools = _toolDefinitions.GetProperty("tools").EnumerateArray();
        var body = tools.Single(tool => tool.GetProperty("name").GetString() == "replace_method_body");
        var declaration = tools.Single(tool => tool.GetProperty("name").GetString() == "replace_method");
        Assert.Contains("NEVER a complete method declaration", body.GetProperty("description").GetString());
        Assert.Contains("replace_method", body.GetProperty("description").GetString());
        Assert.True(body.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("newBody", out _));
        Assert.Contains("No prior read is required", body.GetProperty("description").GetString());
        Assert.Contains("No prior read is required", declaration.GetProperty("description").GetString());
        Assert.True(declaration.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("newMethod", out _));
    }

    [Then(@"MCP indexing details are ""(.*)""")]
    public void ThenIndexingVisibility(string state)
    {
        string[] administrationTools = ["get_repository_index_status", "get_repository_configuration", "preview_repository_index"];
        var methodTool = _toolDefinitions.GetProperty("tools").EnumerateArray()
            .Single(tool => tool.GetProperty("name").GetString() == "get_method_body");
        var properties = methodTool.GetProperty("inputSchema").GetProperty("properties");
        Assert.True(properties.TryGetProperty("relativePath", out _));
        Assert.True(properties.TryGetProperty("name", out _));
        Assert.False(properties.TryGetProperty("snapshotId", out _));
        Assert.DoesNotContain("contentHash", _toolDefinitions.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expectedContentHash", _toolDefinitions.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var codeTools = _toolDefinitions.GetProperty("tools").EnumerateArray()
            .Where(tool => !administrationTools.Contains(tool.GetProperty("name").GetString())
                && tool.GetProperty("name").GetString() != "get_server_info");
        Assert.All(codeTools, tool => AssertNoIndexingDetails(tool.GetRawText()));
        Assert.Equal(_advertisedTools.Length, _advertisedTools.Distinct(StringComparer.Ordinal).Count());

        if (state == "hidden")
        {
            Assert.All(administrationTools, tool => Assert.DoesNotContain(tool, _advertisedTools));
            Assert.True(properties.TryGetProperty("relativePath", out _));
            Assert.True(properties.TryGetProperty("name", out _));
            Assert.False(properties.TryGetProperty("symbolId", out _));
            Assert.False(properties.TryGetProperty("snapshotId", out _));
            AssertNoIndexingDetails(_toolDefinitions.GetRawText());
            AssertNoIndexingDetails(_serverInformation);
        }
        else
        {
            Assert.Equal("exposed", state);
            Assert.All(administrationTools, tool => Assert.Contains(tool, _advertisedTools));
            Assert.False(properties.TryGetProperty("symbolId", out _));
            Assert.Contains("snapshot", _serverInformation, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertNoIndexingDetails(string text)
    {
        foreach (var term in new[] { "index", "snapshot", "sqlite", "recordedRevision", "semanticLanguage", "contentHash", "checksum", "fingerprint", "symbolId" })
        {
            Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
