using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises code tools and optional administration against the same checkout over stdio.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private readonly List<JsonElement> _codeReplies = [];
    private JsonElement _codeFailure;
    private string? _sourceBeforeCodeWrite;

    [When("default MCP navigates Worker and validates Run")]
    public async Task WhenNavigateDefaultCode()
    {
        await using var client = await StartCodeClientAsync();
        var search = await CodeCallAsync(client, "find_symbols", new { name = "Worker" });
        var type = Assert.Single(search.GetProperty("symbols").EnumerateArray());
        var members = await CodeCallAsync(client, "get_type_members", new
        {
            relativePath = "Worker.cs", name = "Worker", kind = "method", maxResults = 1
        });
        Assert.True(members.GetProperty("hasMore").GetBoolean());
        var method = Assert.Single(members.GetProperty("members").EnumerateArray());
        Assert.Equal("Run", method.GetProperty("name").GetString());
        var next = await CodeCallAsync(client, "get_type_members", new
        {
            relativePath = "Worker.cs", name = "Worker", kind = "method", maxResults = 1,
            offset = members.GetProperty("nextOffset").GetInt64()
        });
        Assert.Equal("Call", Assert.Single(next.GetProperty("members").EnumerateArray()).GetProperty("name").GetString());

        var usages = await CodeCallAsync(client, "find_usages", new { relativePath = "Worker.cs", name = "Worker.Run" });
        Assert.Single(usages.GetProperty("usages").EnumerateArray());
        var body = await CodeCallAsync(client, "get_method_body", new { relativePath = "Worker.cs", name = "Run" });
        Assert.Contains("=> 1;", body.GetProperty("body").GetProperty("text").GetString());
        var validation = await CodeCallAsync(client, "validate_method_body", new
        {
            relativePath = "Worker.cs", name = "Run",
            newBody = "{ return 2 }"
        });
        Assert.False(validation.GetProperty("isValid").GetBoolean());
    }

    [When(@"default MCP replaces Run with ""(.*)""")]
    [When("default MCP replaces Run with:")]
    public async Task WhenDefaultCodeWrites(string newBody)
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var written = await CodeCallAsync(client, "replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", newBody
        });
        Assert.Equal("Worker.cs", written.GetProperty("methodLocation").GetProperty("relativePath").GetString());
        var diagnostics = await CodeCallAsync(client, "get_file_diagnostics", new { relativePath = "Worker.cs" });
        Assert.True(diagnostics.GetProperty("isValid").GetBoolean());
    }

    [When(@"default MCP validates and replaces Run with ""(.*)""")]
    public async Task WhenDefaultCodeValidatesAndWrites(string newBody)
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var source = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        var arguments = new { relativePath = "Worker.cs", name = "Run", newBody };
        var validation = await CodeCallAsync(client, "validate_method_body", arguments);
        Assert.True(validation.GetProperty("isValid").GetBoolean());
        Assert.Equal(source, File.ReadAllText(Path.Combine(_root, "Worker.cs")));

        await CodeCallAsync(client, "replace_method_body", arguments);
        var diagnostics = await CodeCallAsync(client, "get_file_diagnostics", new { arguments.relativePath });
        Assert.True(diagnostics.GetProperty("isValid").GetBoolean());
        Assert.Contains("public int Call() => Run();", File.ReadAllText(Path.Combine(_root, "Worker.cs")));
    }

    [When(@"default MCP edits the current method after ""(.*)""")]
    public async Task WhenDefaultCodeSelectionChanges(string change)
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var body = await CodeCallAsync(client, "get_method_body", new { relativePath = "Worker.cs", name = "Run" });
        await ChangeCodeSelectionAsync(change);
        await CodeCallAsync(client, "replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run",
            newBody = "{ return 42; }"
        });
    }

    [When(@"""(.*)"" MCP replaces Run after ""(.*)""")]
    public async Task WhenBodyEditNeedsNoRead(string mode, string readTool)
    {
        await using var client = await StartCodeClientAsync(allowWrite: true, exposeIndexing: mode == "indexing");
        var address = new { relativePath = "Worker.cs", name = "Run" };
        if (readTool != "no read")
        {
            await CodeCallAsync(client, readTool, address);
        }

        await CodeCallAsync(client, "replace_method_body", new
        {
            address.relativePath, address.name, newBody = "return 42;"
        });
        Assert.Contains("public int Call() => Run();", File.ReadAllText(Path.Combine(_root, "Worker.cs")));
    }

    [When("default MCP searches an unprepared repository")]
    public async Task WhenDefaultCodeHasNoData()
    {
        await using var client = await StartCodeClientAsync();
        _codeFailure = await client.CallAsync("find_symbols", new { name = "Worker" });
    }

    [When("MCP searches with indexing exposure enabled")]
    public async Task WhenDetailedCodeSearch()
    {
        await using var client = await StartCodeClientAsync(exposeIndexing: true);
        await CodeCallAsync(client, "find_symbols", new { name = "Worker" });
        await CodeCallAsync(client, "get_repository_index_status", new { });
        await CodeCallAsync(client, "preview_repository_index", new { maxFiles = 1 });
    }

    [Then("code tool replies contain no indexing details")]
    public void ThenCodeRepliesHideIndexing()
    {
        Assert.NotEmpty(_codeReplies);
        Assert.All(_codeReplies, reply => AssertCodeReplyHasNoStorageDetails(reply.GetRawText()));
    }

    [Then("the default MCP write changes the method body")]
    public void ThenDefaultCodeWriteSucceeded() =>
        Assert.Contains("return 42;", File.ReadAllText(Path.Combine(_root, "Worker.cs")));

    [Then(@"source file ""(.*)"" is exactly:")]
    public void ThenSourceFileIsExactly(string relativePath, string expected) =>
        Assert.Equal(expected, File.ReadAllText(Path.Combine(_root, relativePath)));

    [Then("default MCP refuses the stale selection without exposing indexing details")]
    public void ThenDefaultCodeFailure()
    {
        Assert.True(_codeFailure.GetProperty("isError").GetBoolean());
        var text = _codeFailure.GetProperty("content")[0].GetProperty("text").GetString()!;
        AssertCodeReplyHasNoStorageDetails(text);
        Assert.DoesNotContain("Call find_symbols", text);
    }

    [Then("the rejected default MCP write leaves the source unchanged")]
    public void ThenDefaultCodeWriteRejected() =>
        Assert.Equal(_sourceBeforeCodeWrite, File.ReadAllText(Path.Combine(_root, "Worker.cs")));

    [Then("only the administration replies include indexing metadata")]
    public void ThenDetailedCodeReply()
    {
        AssertCodeReplyHasNoStorageDetails(_codeReplies[0].GetRawText());
        Assert.True(_codeReplies[1].TryGetProperty("snapshot", out _));
        Assert.True(_codeReplies[2].TryGetProperty("semanticIndexingLanguage", out _));
    }

    [When("default MCP reads and edits Run across a refresh")]
    public async Task WhenEditAfterRefresh()
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        await CodeCallAsync(client, "get_method_body", new { relativePath = "Worker.cs", name = "Run" });
        await WhenIndex();
        await CodeCallAsync(client, "replace_method_body", new { relativePath = "Worker.cs", name = "Run", newBody = "{ return 42; }" });
    }

    [When("default MCP edits Run twice across a server restart without navigation")]
    public async Task WhenEditWithoutNavigation()
    {
        await WhenDefaultCodeWrites("{ return 42; }");
        await WhenDefaultCodeWrites("{ return 43; }");
        Assert.Contains("return 43;", File.ReadAllText(Path.Combine(_root, "Worker.cs")));
    }

    [When("default MCP selects overloaded methods by their current source position")]
    public async Task WhenSelectOverloads()
    {
        WriteSource("Worker.cs", "class Worker\n{\n    public int Run() => 1;\n    public int Run(int x) => x;\n}\n");
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var ambiguous = await client.CallAsync("get_method_body", new { relativePath = "Worker.cs", name = "Run" });
        Assert.True(ambiguous.GetProperty("isError").GetBoolean());
        Assert.Contains("Several methods match", ambiguous.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.DoesNotContain("find_symbols", ambiguous.GetRawText());
        var body = await CodeCallAsync(client, "get_method_body", new
        {
            relativePath = "Worker.cs", name = "Worker.Run", line = 4, column = 16
        });
        Assert.Contains("=> x;", body.GetProperty("body").GetProperty("text").GetString());
        await CodeCallAsync(client, "replace_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", line = 4, column = 16, newBody = "{ return x + 1; }"
        });
        var source = File.ReadAllText(Path.Combine(_root, "Worker.cs"));
        Assert.Contains("Run() => 1;", source);
        Assert.Contains("return x + 1;", source);
    }

    [When("default MCP continues navigation after a refresh")]
    public async Task WhenContinueNavigationAfterRefresh()
    {
        await using var client = await StartCodeClientAsync();
        var page = await CodeCallAsync(client, "get_type_members", new { relativePath = "Worker.cs", name = "Worker", maxResults = 1 });
        await WhenIndex();
        _codeFailure = await client.CallAsync("get_type_members", new
        {
            relativePath = "Worker.cs", name = "Worker", offset = page.GetProperty("nextOffset").GetInt64()
        });
    }

    [When("default MCP overlaps method body pages across a source edit")]
    public async Task WhenOverlapMethodBodyPages()
    {
        await using var client = await StartCodeClientAsync();
        var first = await CodeCallAsync(client, "get_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", maxCharacters = 2
        });
        var next = await CodeCallAsync(client, "get_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", offset = first.GetProperty("nextOffset").GetInt64()
        });
        Assert.Equal("=> 1;", first.GetProperty("body").GetProperty("text").GetString() +
            next.GetProperty("body").GetProperty("text").GetString());
        WriteSource("Worker.cs", "class Worker\n{\n    public int Run() => 2;\n}\n");
        await CodeCallAsync(client, "get_method_body", new { relativePath = "Worker.cs", name = "Run", maxCharacters = 2 });
        _codeFailure = await client.CallAsync("get_method_body", new
        {
            relativePath = "Worker.cs", name = "Run", offset = first.GetProperty("nextOffset").GetInt64()
        });
    }

    private Task<McpCodeClient> StartCodeClientAsync(bool allowWrite = false, bool exposeIndexing = false) =>
        McpCodeClient.StartAsync(_root, Paths.Root, allowWrite, exposeIndexing, Token);

    private async Task<JsonElement> CodeCallAsync(McpCodeClient client, string name, object arguments)
    {
        var content = McpCodeClient.ReadContent(await client.CallAsync(name, arguments));
        _codeReplies.Add(content);
        return content;
    }

    private async Task ChangeCodeSelectionAsync(string change)
    {
        switch (change)
        {
            case "branch switch":
                await WhenSwitchBranch("other");
                break;
            case "source edit":
                WriteSource("Worker.cs", "class Worker { public int Run() => 99; }");
                break;
            default:
                throw new ArgumentException(change);
        }
    }


    private void AssertCodeReplyHasNoStorageDetails(string text)
    {
        // The checkout path itself may contain words such as "IndexTests".
        var encodedRoot = JsonSerializer.Serialize(_root)[1..^1];
        text = text.Replace(encodedRoot, "<repository>", StringComparison.Ordinal)
            .Replace(_root, "<repository>", StringComparison.Ordinal);

        foreach (var term in new[] { "index", "snapshot", "sqlite", "recordedRevision", "semanticLanguage", "contentHash", "checksum", "fingerprint", "symbolId", "expired symbol" })
        {
            Assert.DoesNotContain(term, text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
