using System.Diagnostics;
using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Verifies diagnostics registration, argument binding and JSON results over real MCP stdio.</summary>
public sealed partial class ServeRepositorySteps
{
    private readonly List<JsonElement> _wireDiagnostics = [];

    [When("I diagnose a malformed file and its class through read-only MCP")]
    public async Task WhenDiagnoseThroughMcp()
    {
        File.WriteAllText(Path.Combine(SelectedRoot, "Selected.cs"), "class Example { int Run() => ; }");
        using var process = Process.Start(CreateStartInfo(["serve", SelectedRoot], null))!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await SendAsync(process,
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"diagnostics-test","version":"1.0"}}}
                """, timeout.Token);
            await ReceiveAsync(process, 1, timeout.Token);
            await SendAsync(process, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", timeout.Token);

            await SendAsync(process,
                """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"get_file_diagnostics","arguments":{"relativePath":"Selected.cs"}}}""",
                timeout.Token);
            _wireDiagnostics.Add(ReadDiagnosticContent(await ReceiveAsync(process, 2, timeout.Token)));

            await SendAsync(process,
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_type_diagnostics","arguments":{"relativePath":"Selected.cs","line":1,"column":7,"expectedTypeName":"Example"}}}""",
                timeout.Token);
            _wireDiagnostics.Add(ReadDiagnosticContent(await ReceiveAsync(process, 3, timeout.Token)));
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

    [Then("MCP returns precise current-file and class syntax errors")]
    public void ThenMcpDiagnostics()
    {
        Assert.Equal(2, _wireDiagnostics.Count);
        Assert.All(_wireDiagnostics, result =>
        {
            Assert.False(result.GetProperty("isValid").GetBoolean());
            Assert.Equal("Selected.cs", result.GetProperty("relativePath").GetString());
            AssertNoIndexingDetails(result.GetRawText());
            Assert.Equal(1, result.GetProperty("totalDiagnostics").GetInt32());
            Assert.False(result.GetProperty("hasMore").GetBoolean());
            var diagnostic = Assert.Single(result.GetProperty("diagnostics").EnumerateArray());
            Assert.Equal("CS1525", diagnostic.GetProperty("id").GetString());
            Assert.Equal("error", diagnostic.GetProperty("severity").GetString());
            Assert.Equal(1, diagnostic.GetProperty("location").GetProperty("line").GetInt32());
            Assert.Equal(30, diagnostic.GetProperty("location").GetProperty("column").GetInt32());
        });
        Assert.Equal(7, _wireDiagnostics[1].GetProperty("typeLocation").GetProperty("column").GetInt32());
    }

    private static JsonElement ReadDiagnosticContent(JsonElement response)
    {
        Assert.False(response.TryGetProperty("isError", out var error) && error.GetBoolean(), response.ToString());
        using var content = JsonDocument.Parse(response.GetProperty("content")[0].GetProperty("text").GetString()!);
        return content.RootElement.Clone();
    }
}
