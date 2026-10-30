using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks single-call edit diagnostics, unchanged files on failure, and direct retries over MCP.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private McpCodeClient? _validatedEditClient;
    private string? _invalidDeclaration;

    [When(@"""(.*)"" MCP directly replaces Run with a missing semicolon")]
    public async Task WhenDirectEditHasMissingSemicolon(string mode)
    {
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        _validatedEditClient = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
        _codeFailure = await _validatedEditClient.CallAsync("replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", newBody = "return 42"
        });
    }

    [Then(@"the edit reports CS1002 at proposed file position (\d+):(\d+) without writing")]
    public void ThenDirectEditReportsLocation(int line, int column)
    {
        var message = EditFailureMessage();
        Assert.Contains($"CS1002 Worker.cs:{line}:{column} ; expected", message);
        Assert.Contains("retry replace_method_body", message);
        Assert.DoesNotContain("validate_method_body", message);
        ThenDefaultCodeWriteRejected();
    }

    [When("the same MCP client retries with a corrected body")]
    public async Task WhenDirectEditIsCorrected()
    {
        await CodeCallAsync(_validatedEditClient!, "replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", newBody = "return 42;"
        });
    }

    [Then("only the Run body is replaced")]
    public void ThenDirectEditPreservesOtherMembers()
    {
        var source = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        Assert.Equal(_sourceBeforeCodeWrite!.Replace("=> 1;", "{\n        return 42;\n    }", StringComparison.Ordinal), source);
    }

    [When(@"MCP directly replaces Run with (\d+) syntax errors")]
    public async Task WhenDirectEditHasManyErrors(int count)
    {
        var statements = Enumerable.Range(0, count).Select(index => $"var value{index} = ;");
        await RejectDirectBodyAsync("{\n" + string.Join("\n", statements) + "\n}");
    }

    [Then(@"the edit reports (\d+) errors and indicates (\d+) additional errors")]
    public void ThenDirectEditFeedbackIsBounded(int returned, int remaining)
    {
        var message = EditFailureMessage();
        Assert.Equal(returned, message.Split('\n').Count(line => line.StartsWith("CS", StringComparison.Ordinal)));
        Assert.Contains($"{remaining} more syntax errors.", message);
    }

    [When("MCP directly supplies a complete declaration as the Run body")]
    public Task WhenDirectEditHasWrongShape() => RejectDirectBodyAsync("public int Run() => 42;");

    [Then("the edit explains that only a body can be replaced")]
    public void ThenDirectEditExplainsBodyShape()
    {
        var message = EditFailureMessage();
        Assert.Contains("CS4L0001", message);
        Assert.Contains("Do not include the method signature; use replace_method", message);
    }

    [When(@"""(.*)"" MCP directly replaces Run with this declaration:")]
    public async Task WhenDirectDeclarationIsInvalid(string mode, string newMethod)
    {
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        _invalidDeclaration = newMethod;
        _validatedEditClient = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
        _codeFailure = await _validatedEditClient.CallAsync("replace_method", new
        {
            relativePath = "Worker.cs", name = "Run", newMethod
        });
    }

    [Then(@"the declaration edit reports CS1010 at proposed file position (\d+):(\d+) without writing")]
    public void ThenDeclarationEditReportsStringError(int line, int column)
    {
        var message = EditFailureMessage();
        Assert.Contains($"CS1010 Worker.cs:{line}:{column} Newline in constant", message);
        Assert.Contains("CS1026", message);
        Assert.Contains("retry replace_method.", message);
        Assert.DoesNotContain("retry replace_method_body", message);
        Assert.DoesNotContain("Supply exactly one", message);
        ThenDefaultCodeWriteRejected();
    }

    [When("the same MCP client fixes the string and retries the declaration edit")]
    public async Task WhenDirectDeclarationIsCorrected()
    {
        var corrected = _invalidDeclaration!.Replace("""\\u0022""", "\" +", StringComparison.Ordinal);
        Assert.NotEqual(_invalidDeclaration, corrected);
        await CodeCallAsync(_validatedEditClient!, "replace_method", new
        {
            relativePath = "Worker.cs", name = "Run", newMethod = corrected
        });
    }

    [Then("the corrected declaration is written and the other method is preserved")]
    public void ThenCorrectedDeclarationIsWritten()
    {
        var source = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        Assert.Contains("""errors.MarkupLine("[red]tinyharness:[/] " + Markup.Escape(exception.Message));""", source);
        Assert.Contains("return 42;", source);
        Assert.Contains("public int Keep() => 7;", source);
    }

    [When(@"MCP directly replaces the Run declaration with (\d+) syntax errors")]
    public async Task WhenDeclarationHasManyErrors(int count)
    {
        var statements = Enumerable.Range(0, count).Select(index => $"    var value{index} = ;");
        var newMethod = "public int Run()\n{\n" + string.Join("\n", statements) + "\n}";
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        await using var client = await StartCodeClientAsync(allowWrite: true);
        _codeFailure = await client.CallAsync("replace_method", new
        {
            relativePath = "Worker.cs", name = "Run", newMethod
        });
    }

    [AfterScenario(Order = -100)]
    public async Task DisposeValidatedEditClient()
    {
        if (_validatedEditClient is not null)
        {
            await _validatedEditClient.DisposeAsync();
        }
    }

    private async Task RejectDirectBodyAsync(string newBody)
    {
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        await using var client = await StartCodeClientAsync(allowWrite: true);
        _codeFailure = await client.CallAsync("replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", newBody
        });
    }

    private string EditFailureMessage()
    {
        Assert.True(_codeFailure.GetProperty("isError").GetBoolean());
        var message = _codeFailure.GetProperty("content")[0].GetProperty("text").GetString()!;
        Assert.Contains("No edit applied.", message);
        AssertCodeReplyHasNoStorageDetails(message);
        return message;
    }
}
