using System.Text;
using System.Text.Json;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises direct full-declaration edits and consistent source pagination through actual MCP calls.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private JsonElement _declarationWrite;

    [When(@"""(.*)"" MCP reads the first Run declaration in pages and replaces it with:")]
    public async Task WhenReplaceCompleteMethod(string mode, string newMethod)
    {
        var method = await FirstRunAsync();
        var arguments = MethodAddress(method);
        arguments["maxCharacters"] = 11;
        arguments["offset"] = 0L;
        await using var client = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
        var text = new StringBuilder();
        JsonElement read;
        do
        {
            read = await CodeCallAsync(client, "get_method", arguments);
            text.Append(read.GetProperty("text").GetString());
            if (!read.TryGetProperty("nextOffset", out var nextOffset) || nextOffset.ValueKind == JsonValueKind.Null)
            {
                break;
            }

            arguments["offset"] = nextOffset.GetInt64();
        } while (true);

        Assert.Equal("    [Old]\n    public int Run(int value) => value;", text.ToString());
        var replacement = MethodAddress(method);
        replacement["newMethod"] = newMethod;
        _declarationWrite = await CodeCallAsync(client, "replace_method", replacement);
        ThenCodeRepliesHideIndexing();

        var diagnostics = await CodeCallAsync(client, "get_file_diagnostics", new { relativePath = "Worker.cs" });
        Assert.True(diagnostics.GetProperty("isValid").GetBoolean());
    }

    [Then(@"the declaration edit returns the new identifier position for ""(.*)""")]
    public async Task ThenDeclarationPosition(string name)
    {
        var methods = await CurrentDeclarationsAsync();
        var method = Assert.Single(methods, symbol => symbol.Kind == "method" && symbol.Name == name);
        var location = _declarationWrite.GetProperty("methodLocation");
        Assert.Equal(method.Location.Line, location.GetProperty("line").GetInt32());
        Assert.Equal(method.Location.Column, location.GetProperty("column").GetInt32());
        Assert.Equal("Worker.cs", location.GetProperty("relativePath").GetString());
    }

    [When(@"default MCP tries to replace the first Run body with ""(.*)""")]
    public async Task WhenCompleteMethodSentAsBody(string newBody)
    {
        var arguments = MethodAddress(await FirstRunAsync());
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        await using var client = await StartCodeClientAsync(allowWrite: true);
        arguments["newBody"] = newBody;
        var validation = await CodeCallAsync(client, "validate_method_body", arguments);
        Assert.False(validation.GetProperty("isValid").GetBoolean());
        _codeFailure = await client.CallAsync("replace_method_body", arguments);
    }

    [When(@"""(.*)"" MCP replaces the first Run declaration after ""(.*)""")]
    public async Task WhenCompleteMethodEditIsDirect(string mode, string change)
    {
        var arguments = MethodAddress(await FirstRunAsync());
        await using var client = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
        if (change != "no read")
        {
            await CodeCallAsync(client, change == "body read" ? "get_method_body" : "get_method", arguments);
        }

        if (change == "source edit")
        {
            var path = Path.Combine(_root, "Worker.cs");
            File.WriteAllText(path, File.ReadAllText(path).Replace("=> value;", "=> value + 1;", StringComparison.Ordinal));
        }
        else if (change == "branch switch")
        {
            await WhenSwitchBranch("other");
        }

        arguments["newMethod"] = "public string Execute(string text) => text;";
        if (change == "server restart")
        {
            await using var restarted = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
            _declarationWrite = await CodeCallAsync(restarted, "replace_method", arguments);
        }
        else
        {
            _declarationWrite = await CodeCallAsync(client, "replace_method", arguments);
        }

        var source = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        Assert.Contains("public string Execute(string text) => text;", source);
        Assert.Contains("public int Run() => 7;", source);
        Assert.Contains("// Keep this comment.", source);
    }

    [When("default MCP tries to replace a method with multiple declarations without reading")]
    public async Task WhenInvalidDeclarationIsRejected()
    {
        var arguments = MethodAddress(await FirstRunAsync());
        arguments["newMethod"] = "public int Run() => 42; public int Inject() => 7;";
        _sourceBeforeCodeWrite = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        await using var client = await StartCodeClientAsync(allowWrite: true);
        _codeFailure = await client.CallAsync("replace_method", arguments);
    }

    [When("default MCP uses a complete-method continuation to read a body")]
    public async Task WhenMethodPageUsedForBody()
    {
        var arguments = MethodAddress(await FirstRunAsync());
        arguments["maxCharacters"] = 5;
        await using var client = await StartCodeClientAsync();
        var read = await CodeCallAsync(client, "get_method", arguments);
        arguments["offset"] = read.GetProperty("nextOffset").GetInt64();
        _codeFailure = await client.CallAsync("get_method_body", arguments);
    }

    [When("default MCP continues a complete-method read after changing its source")]
    public async Task WhenCompleteMethodPageChanges()
    {
        var arguments = MethodAddress(await FirstRunAsync());
        arguments["maxCharacters"] = 5;
        await using var client = await StartCodeClientAsync();
        var read = await CodeCallAsync(client, "get_method", arguments);
        arguments["offset"] = read.GetProperty("nextOffset").GetInt64();
        var path = Path.Combine(_root, "Worker.cs");
        File.WriteAllText(path, File.ReadAllText(path).Replace("=> value;", "=> value + 1;", StringComparison.Ordinal));
        _codeFailure = await client.CallAsync("get_method", arguments);
    }

    [Then(@"the declaration request fails with ""(.*)""")]
    public void ThenDeclarationFailure(string message)
    {
        Assert.True(_codeFailure.GetProperty("isError").GetBoolean());
        Assert.Contains(message, _codeFailure.GetProperty("content")[0].GetProperty("text").GetString());
    }

    private async Task<SymbolDefinition> FirstRunAsync() =>
        (await CurrentDeclarationsAsync()).First(symbol => symbol.Kind == "method" && symbol.Name == "Run");

    private Task<IReadOnlyList<SymbolDefinition>> CurrentDeclarationsAsync() =>
        new CSharpSourceAnalyzer().AnalyzeAsync(
            new SourceDocument("Worker.cs", File.ReadAllText(Path.Combine(_root, "Worker.cs"))), Token);

    private static Dictionary<string, object?> MethodAddress(SymbolDefinition method) =>
        new()
        {
            ["relativePath"] = "Worker.cs",
            ["name"] = "Worker.Run",
            ["line"] = method.Location.Line,
            ["column"] = method.Location.Column
        };
}
