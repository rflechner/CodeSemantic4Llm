using System.Text.Json;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

public sealed partial class RepositoryIndexingSteps
{
    [When("MCP inspects and edits the nested Worker source")]
    public async Task WhenInspectAbsolutePaths()
    {
        await using var client = await StartCodeClientAsync(allowWrite: true, exposeIndexing: true);
        const string relativePath = "nested folder/Worker.cs";
        var method = new { relativePath, name = "Run" };
        await CodeCallAsync(client, "find_symbols", new { name = "Worker" });
        await CodeCallAsync(client, "get_type_members", new { relativePath, name = "Worker" });
        await CodeCallAsync(client, "find_usages", method);
        await CodeCallAsync(client, "get_method", method);
        await CodeCallAsync(client, "get_method_body", method);
        await CodeCallAsync(client, "get_file_diagnostics", new { relativePath });
        await CodeCallAsync(client, "get_type_diagnostics", new { relativePath, line = 1, column = 7, expectedTypeName = "Worker" });
        await CodeCallAsync(client, "get_method_diagnostics", new { relativePath, line = 3, column = 16, expectedMethodName = "Run" });
        await CodeCallAsync(client, "validate_method_body", new { relativePath, name = "Run", newBody = "{ return 2 }" });
        await CodeCallAsync(client, "preview_repository_index", new { });
        await CodeCallAsync(client, "replace_method_body", new { relativePath, name = "Run", newBody = "return 2;" });
        await CodeCallAsync(client, "replace_method", new { relativePath, name = "Run", newMethod = "public int Run() => 3;" });
    }

    [Given("MCP source paths are configured as \"(.*)\"")]
    public async Task GivenMcpPathFormat(string format)
    {
        var path = Path.Combine(_root, "codesemantic4llm.yaml");
        var lines = (await File.ReadAllLinesAsync(path))
            .Where(line => !line.StartsWith("mcpPaths:", StringComparison.Ordinal));
        await File.WriteAllLinesAsync(path, lines.Append($"mcpPaths: {format}"));
    }

    [When("MCP changes path formats between calls without restarting")]
    public async Task WhenPathFormatChanges()
    {
        await using var client = await StartCodeClientAsync();
        foreach (var format in new[] { "both", "relative", "absolute", "both" })
        {
            await GivenMcpPathFormat(format);
            var reply = await CodeCallAsync(client, "find_symbols", new { name = "Worker" });
            Assert.True(AssertSelectedPaths(reply, format) > 0);
        }
    }

    [Then("every returned source path uses only \"(.*)\" fields")]
    public void ThenSelectedPaths(string format)
    {
        Assert.NotEmpty(_codeReplies);
        Assert.All(_codeReplies, reply => Assert.True(AssertSelectedPaths(reply, format) > 0));
    }

    private int AssertSelectedPaths(JsonElement element, string format)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Sum(item => AssertSelectedPaths(item, format));
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var count = 0;
        var hasRelative = element.TryGetProperty("relativePath", out var relative);
        var hasAbsolute = element.TryGetProperty("absolutePath", out var absolute);
        if (hasRelative || hasAbsolute)
        {
            Assert.Equal(format != "absolute", hasRelative);
            Assert.Equal(format != "relative", hasAbsolute);
            if (hasRelative)
            {
                Assert.Equal("nested folder/Worker.cs", relative.GetString());
            }

            if (hasAbsolute)
            {
                Assert.Equal(Path.GetFullPath("nested folder/Worker.cs", _root), absolute.GetString());
            }

            count++;
        }

        return count + element.EnumerateObject().Sum(property => AssertSelectedPaths(property.Value, format));
    }

    [Then("every returned relative source path has its matching absolute path")]
    public void ThenAbsolutePathsMatch()
    {
        Assert.NotEmpty(_codeReplies);
        Assert.All(_codeReplies, reply => Assert.True(AssertAbsolutePaths(reply) > 0));
    }

    private int AssertAbsolutePaths(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Sum(AssertAbsolutePaths);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var count = 0;
        if (element.TryGetProperty("relativePath", out var relative))
        {
            var absolute = element.GetProperty("absolutePath").GetString();
            Assert.Equal(Path.GetFullPath(relative.GetString()!, _root), absolute);
            Assert.NotNull(absolute);
            Assert.True(Path.IsPathFullyQualified(absolute));
            count++;
        }

        return count + element.EnumerateObject().Sum(property => AssertAbsolutePaths(property.Value));
    }
}
