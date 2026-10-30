using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks that the default interface handles source versions without client-managed metadata.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private const string MalformedBlock = "{ var first = ; var second = ; var third = ; return 1; }";

    [When(@"default MCP reads all ""(.*)"" diagnostics without file metadata")]
    public async Task WhenReadDefaultDiagnostics(string scope)
    {
        await using var client = await StartCodeClientAsync();
        var arguments = new Dictionary<string, object?> { ["maxDiagnostics"] = 1 };
        var tool = SetUpDefaultDiagnosticQuery(scope, arguments);
        long offset = 0;
        var diagnosticCount = 0;

        do
        {
            arguments["offset"] = offset;
            var report = await CodeCallAsync(client, tool, arguments);
            Assert.False(report.GetProperty("isValid").GetBoolean());
            Assert.Equal(3, report.GetProperty("totalDiagnostics").GetInt32());
            Assert.Single(report.GetProperty("diagnostics").EnumerateArray());
            diagnosticCount++;
            offset = report.TryGetProperty("nextOffset", out var next) ? next.GetInt64() : 0;
        } while (offset != 0);

        Assert.Equal(3, diagnosticCount);
    }

    [When("default MCP overlaps diagnostic reads across a source edit")]
    public async Task WhenOverlapDefaultDiagnostics()
    {
        WriteMalformedMethod();
        await using var client = await StartCodeClientAsync();
        var first = await CodeCallAsync(client, "get_file_diagnostics", new { relativePath = "Worker.cs", maxDiagnostics = 1 });
        WriteMalformedMethod("\n");
        var second = await CodeCallAsync(client, "get_file_diagnostics", new { relativePath = "Worker.cs", maxDiagnostics = 1 });

        _codeFailure = await client.CallAsync("get_file_diagnostics", new
        {
            relativePath = "Worker.cs", maxDiagnostics = 1, offset = first.GetProperty("nextOffset").GetInt64()
        });
        var continued = await CodeCallAsync(client, "get_file_diagnostics", new
        {
            relativePath = "Worker.cs", maxDiagnostics = 1, offset = second.GetProperty("nextOffset").GetInt64()
        });
        Assert.Equal(3, continued.GetProperty("totalDiagnostics").GetInt32());
    }

    [When("default MCP uses a file diagnostic continuation for a type")]
    public async Task WhenReuseDiagnosticPageForDifferentScope()
    {
        WriteMalformedMethod();
        await using var client = await StartCodeClientAsync();
        var first = await CodeCallAsync(client, "get_file_diagnostics", new { relativePath = "Worker.cs", maxDiagnostics = 1 });
        _codeFailure = await client.CallAsync("get_type_diagnostics", new
        {
            relativePath = "Worker.cs", line = 1, column = 7, expectedTypeName = "Worker",
            maxDiagnostics = 1, offset = first.GetProperty("nextOffset").GetInt64()
        });
    }

    [When("default MCP validates a method then attempts to edit it without reading")]
    public async Task WhenValidateThenWriteWithoutRead()
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var arguments = new { relativePath = "Worker.cs", name = "Run", newBody = "{ return 42; }" };
        var report = await CodeCallAsync(client, "validate_method_body", arguments);
        Assert.True(report.GetProperty("isValid").GetBoolean());
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        _codeFailure = await client.CallAsync("replace_method_body", arguments);
    }

    [Then("default MCP refuses the changed diagnostic page")]
    [Then("default MCP asks to restart pagination")]
    public void ThenChangedDiagnosticPageRejected()
    {
        Assert.True(_codeFailure.GetProperty("isError").GetBoolean());
        var message = _codeFailure.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("offset 0", message);
        AssertCodeReplyHasNoStorageDetails(message);
    }

    [Then("default MCP asks to read the method before editing")]
    public void ThenMethodReadRequired()
    {
        Assert.True(_codeFailure.GetProperty("isError").GetBoolean());
        var message = _codeFailure.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("get_method_body", message);
        AssertCodeReplyHasNoStorageDetails(message);
    }

    private string SetUpDefaultDiagnosticQuery(
        string scope, Dictionary<string, object?> arguments)
    {
        if (scope == "proposed")
        {
            arguments["relativePath"] = "Worker.cs";
            arguments["name"] = "Run";
            arguments["newBody"] = MalformedBlock;
            return "validate_method_body";
        }

        WriteMalformedMethod();
        arguments["relativePath"] = "Worker.cs";
        if (scope == "file")
        {
            return "get_file_diagnostics";
        }

        arguments["line"] = scope == "type" ? 1 : 3;
        arguments["column"] = scope == "type" ? 7 : 16;
        arguments[scope == "type" ? "expectedTypeName" : "expectedMethodName"] = scope == "type" ? "Worker" : "Run";
        return scope == "type" ? "get_type_diagnostics" : "get_method_diagnostics";
    }

    private void WriteMalformedMethod(string prefix = "") =>
        WriteSource("Worker.cs", prefix + "class Worker\n{\n    public int Run() " + MalformedBlock + "\n}");
}
