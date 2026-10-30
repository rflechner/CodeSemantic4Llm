using System.Diagnostics;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Confirms tool discovery follows the operator's process-startup permission.</summary>
public sealed partial class ServeRepositorySteps
{
    private string[] _advertisedTools = [];

    [When(@"I list MCP tools with ""(.*)"" server startup")]
    public async Task WhenListMcpTools(string mode)
    {
        string[] arguments = mode switch
        {
            "write-enabled" => ["serve", SelectedRoot, "--allow-write"],
            "read-only" => ["serve", SelectedRoot],
            "default" => [],
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
            _advertisedTools = listed.GetProperty("tools").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
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

    [Then("MCP advertises the method body reader")]
    public void ThenMethodReaderAdvertised()
    {
        Assert.Contains("get_method_body", _advertisedTools);
        Assert.Contains("validate_method_body", _advertisedTools);
        Assert.Contains("get_method_diagnostics", _advertisedTools);
        Assert.Contains("find_symbols", _advertisedTools);
        Assert.Contains("get_server_info", _advertisedTools);
    }

    [Then(@"MCP writing tools are ""(.*)""")]
    public void ThenWriteToolsVisibility(string state)
    {
        if (state == "available")
        {
            Assert.Contains("replace_method_body", _advertisedTools);
        }
        else
        {
            Assert.Equal("absent", state);
            Assert.DoesNotContain("replace_method_body", _advertisedTools);
        }
    }
}
