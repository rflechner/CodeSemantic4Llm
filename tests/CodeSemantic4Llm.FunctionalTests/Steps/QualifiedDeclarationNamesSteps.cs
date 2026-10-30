using System.Text.Json;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Domain.Source;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks the same source-address rules against current Roslyn declarations and persisted navigation.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private readonly List<JsonElement> _qualifiedSelectionFailures = [];
    private string _qualifiedSourceBeforeWrite = "";
    private string _expectedCliMethodName = "TinyHarness.Cli.CliApplication.RunAsync";
    private JsonElement _qualifiedMembers;
    private JsonElement _qualifiedUsages;

    [When(@"default MCP reads, validates and edits ""(.*)"" in ""(.*)""")]
    public async Task WhenEditQualifiedMethod(string name, string relativePath)
    {
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var body = await CodeCallAsync(client, "get_method_body", new { relativePath, name });
        Assert.Equal(_expectedCliMethodName, body.GetProperty("method").GetProperty("qualifiedName").GetString());
        Assert.Contains("Task.FromResult(1)", body.GetProperty("body").GetProperty("text").GetString());

        const string newBody = "{ return Task.FromResult(42); }";
        var validation = await CodeCallAsync(client, "validate_method_body", new { relativePath, name, newBody });
        Assert.True(validation.GetProperty("isValid").GetBoolean());
        await CodeCallAsync(client, "replace_method_body", new { relativePath, name, newBody });
    }

    [Then("only the requested qualified method body is changed")]
    public void ThenOnlyQualifiedMethodChanged()
    {
        var source = File.ReadAllText(Path.Combine(_root, "CliApplication.cs"));
        Assert.Contains("RunAsync() { return Task.FromResult(42); }", source);
        Assert.Contains("RunAsync() => Task.FromResult(2);", source);
        Assert.Contains("RunAsync() => 3;", File.ReadAllText(Path.Combine(_root, "Other.cs")));
    }

    [When("default MCP navigates the abbreviated CLI type and method names")]
    public async Task WhenNavigateQualifiedNames()
    {
        await using var client = await StartCodeClientAsync();
        _qualifiedMembers = await CodeCallAsync(client, "get_type_members",
            new { relativePath = "CliApplication.cs", name = "Cli.CliApplication" });
        _qualifiedUsages = await CodeCallAsync(client, "find_usages",
            new { relativePath = "CliApplication.cs", name = "CliApplication.RunAsync" });
    }

    [Then("the selected declarations belong to the requested CLI type")]
    public void ThenQualifiedNavigationTargets()
    {
        Assert.Equal("TinyHarness.Cli.CliApplication", _qualifiedMembers.GetProperty("type").GetProperty("qualifiedName").GetString());
        Assert.Equal(2, _qualifiedMembers.GetProperty("members").GetArrayLength());
        Assert.Equal(_expectedCliMethodName, _qualifiedUsages.GetProperty("target").GetProperty("qualifiedName").GetString());
        Assert.Single(_qualifiedUsages.GetProperty("usages").EnumerateArray());
    }

    [When(@"default MCP selects the CLI method as ""(.*)""")]
    public async Task WhenSelectQualifiedName(string name)
    {
        _qualifiedSourceBeforeWrite = File.ReadAllText(Path.Combine(_root, "CliApplication.cs"));
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var address = new { relativePath = "CliApplication.cs", name };
        var replacement = new { relativePath = "CliApplication.cs", name, newBody = "{ return Task.FromResult(42); }" };
        _qualifiedSelectionFailures.Add(await client.CallAsync("get_method_body", address));
        _qualifiedSelectionFailures.Add(await client.CallAsync("validate_method_body", replacement));
        _qualifiedSelectionFailures.Add(await client.CallAsync("replace_method_body", replacement));
        _qualifiedSelectionFailures.Add(await client.CallAsync("find_usages", address));
    }

    [Then(@"method operations and navigation reject the name with ""(.*)""")]
    public void ThenQualifiedSelectionRejected(string message) => Assert.All(_qualifiedSelectionFailures, response =>
    {
        Assert.True(response.GetProperty("isError").GetBoolean());
        Assert.Contains(message, response.GetProperty("content")[0].GetProperty("text").GetString());
    });

    [Then("the qualified selection leaves the source unchanged")]
    public void ThenQualifiedSourceUnchanged() =>
        Assert.Equal(_qualifiedSourceBeforeWrite, File.ReadAllText(Path.Combine(_root, "CliApplication.cs")));

    [Given(@"the CLI source has ""(.*)"" for the abbreviated method name")]
    public void GivenQualifiedNameCollision(string collision)
    {
        var declarations = collision switch
        {
            "overloaded methods" => """
                namespace First
                {
                    public class CliApplication
                    {
                        public int RunAsync() => 1;
                        public int RunAsync(int value) => 2;
                    }
                }
                """,
            "types in different namespaces" => """
                namespace First { public class CliApplication { public int RunAsync() => 1; } }
                namespace Second { public class CliApplication { public int RunAsync() => 2; } }
                """,
            "global and namespaced types" => """
                public class CliApplication { public int RunAsync() => 1; }
                namespace Second { public class CliApplication { public int RunAsync() => 2; } }
                """,
            _ => throw new ArgumentException(collision)
        };
        WriteSource("CliApplication.cs", declarations);
    }

    [When("default MCP selects the second CLI method by its identifier position")]
    public async Task WhenDisambiguateQualifiedName()
    {
        var source = File.ReadAllText(Path.Combine(_root, "CliApplication.cs"));
        var declarations = await new CSharpSourceAnalyzer().AnalyzeAsync(new SourceDocument("CliApplication.cs", source), Token);
        var method = declarations.Last(symbol => symbol.Kind == "method" && symbol.Name == "RunAsync");
        var address = new { relativePath = "CliApplication.cs", name = "CliApplication.RunAsync",
            line = method.Location.Line, column = method.Location.Column };
        await using var client = await StartCodeClientAsync(allowWrite: true);
        var usages = await CodeCallAsync(client, "find_usages", address);
        Assert.Equal(method.Location.Line, usages.GetProperty("target").GetProperty("location").GetProperty("line").GetInt32());
        var body = await CodeCallAsync(client, "get_method_body", address);
        Assert.Equal("=> 2;", body.GetProperty("body").GetProperty("text").GetString());
        await CodeCallAsync(client, "replace_method_body", new
        {
            address.relativePath, address.name, address.line, address.column, newBody = "{ return 42; }"
        });
    }

    [Then("only the second CLI method is changed")]
    public void ThenOnlySecondQualifiedMethodChanged()
    {
        var source = File.ReadAllText(Path.Combine(_root, "CliApplication.cs"));
        Assert.Contains("RunAsync() => 1;", source);
        Assert.Contains("{ return 42; }", source);
        Assert.DoesNotContain("=> 2;", source);
    }

    [Given("the CLI source has a nested application type")]
    public void GivenNestedCliType()
    {
        WriteSource("CliApplication.cs", """
            using System.Threading.Tasks;
            namespace TinyHarness.Cli;
            public class Host
            {
                public class CliApplication { public Task<int> RunAsync() => Task.FromResult(1); }
                public class OtherCliApplication { public Task<int> RunAsync() => Task.FromResult(2); }
            }
            """);
        _expectedCliMethodName = "TinyHarness.Cli.Host.CliApplication.RunAsync";
    }
}
