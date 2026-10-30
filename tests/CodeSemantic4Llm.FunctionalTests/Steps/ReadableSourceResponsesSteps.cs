using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks the actual MCP text content before decoding its JSON, plus exact source round trips.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private string _readableSourceReply = "";
    private string _readableSourceCode = "";
    private string _readableSourceBefore = "";
    private string _readableSourceTool = "";
    private bool _readableSourceExposesIndexing;

    [When(@"""(.*)"" MCP reads Run using ""(.*)""")]
    public async Task WhenReadSourceForModel(string mode, string reader)
    {
        _readableSourceExposesIndexing = mode == "indexing";
        _readableSourceTool = reader;
        _readableSourceBefore = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        await using var client = await StartCodeClientAsync(exposeIndexing: _readableSourceExposesIndexing);
        var response = await client.CallAsync(reader, new { relativePath = "Worker.cs", name = "Run" });
        _readableSourceReply = response.GetProperty("content")[0].GetProperty("text").GetString()!;
        var read = McpCodeClient.ReadContent(response);
        _readableSourceCode = reader == "get_method"
            ? read.GetProperty("text").GetString()!
            : read.GetProperty("body").GetProperty("text").GetString()!;
    }

    [Then("the model-facing JSON keeps C# punctuation and Unicode readable")]
    public void ThenSourceTextIsReadable()
    {
        // Check what a text-only client passes to the model, not just the decoded source.
        Assert.Contains("""errors.MarkupLine(\"[red]tinyharness:[/] \" + Markup.Escape(exception.Message));""",
            _readableSourceReply);
        Assert.Contains("new List<string>()", _readableSourceReply);
        Assert.Contains("été, l'apostrophe, <tags> and &", _readableSourceReply);
        Assert.Contains("""var literal = @"\u0022\u002B";""", _readableSourceCode);
        Assert.Contains("""var quoted = "\"quoted\"";""", _readableSourceCode);
        AssertCodeReplyHasNoStorageDetails(_readableSourceReply);
    }

    [When("MCP writes the returned code with only the return value changed")]
    public async Task WhenWriteReadableSource()
    {
        var replacement = _readableSourceCode.Replace("return 1;", "return 2;", StringComparison.Ordinal);
        var wholeMethod = _readableSourceTool == "get_method";
        var arguments = new Dictionary<string, object?>
        {
            ["relativePath"] = "Worker.cs",
            ["name"] = "Run",
            [wholeMethod ? "newMethod" : "newBody"] = replacement
        };
        await using var client = await StartCodeClientAsync(allowWrite: true, exposeIndexing: _readableSourceExposesIndexing);
        await CodeCallAsync(client, wholeMethod ? "replace_method" : "replace_method_body", arguments);
    }

    [Then("source encoding and literal escape sequences are preserved")]
    public void ThenLiteralSourceEscapesArePreserved() =>
        Assert.Equal(_readableSourceBefore.Replace("return 1;", "return 2;", StringComparison.Ordinal),
            File.ReadAllText(Path.Combine(_root, "Worker.cs")));
}
