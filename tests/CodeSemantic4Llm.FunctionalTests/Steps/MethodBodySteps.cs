using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Tools;
using Microsoft.Extensions.Configuration;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises actual file changes through indexed method selection and optimistic-concurrency checks.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private MethodBodyReadResult? _readBody;
    private MethodBodyWriteResult? _writeResult;
    private Exception? _methodError;
    private string _completeBody = "";
    private int _bodyPages;

    [When(@"I read the selected method body")]
    public async Task WhenReadBody()
    {
        _readBody = await CreateMethodReader().GetMethodBodyAsync(
            _usageTarget!.Location.RelativePath, _usageTarget.Location.Line, _usageTarget.Location.Column,
            _usageSnapshotId, cancellationToken: Token);
    }

    [When(@"I read the selected method body in pages of (\d+) characters")]
    public async Task WhenReadBodyInPages(int pageSize)
    {
        _completeBody = "";
        _bodyPages = 0;
        var offset = 0;
        do
        {
            var result = await CreateMethodReader().GetMethodBodyAsync(
                _usageTarget!.Location.RelativePath, _usageTarget.Location.Line, _usageTarget.Location.Column,
                _usageSnapshotId, maxCharacters: pageSize, offset: offset, cancellationToken: Token);

            _completeBody += result.Body.Text;
            _bodyPages++;
            if (result.NextOffset is not int next)
            {
                break;
            }

            offset = next;
        } while (true);
    }

    [Then(@"the complete selected body contains ""(.*)""")]
    public void ThenCompleteBodyContains(string content) => Assert.Contains(content, _completeBody);

    [Then("the method body was read in multiple pages")]
    public void ThenReadMultiplePages() => Assert.True(_bodyPages > 1);

    [Then(@"the selected body contains ""(.*)""")]
    public void ThenBodyContains(string content) => Assert.Contains(content, _readBody!.Body.Text);

    [Then("the selected method has an expression body")]
    public void ThenExpressionBody() => Assert.True(_readBody!.Body.IsExpressionBodied);

    [When(@"I replace the selected method body with ""(.*)""")]
    public async Task WhenReplaceBody(string body)
    {
        var location = _usageTarget!.Location;
        _writeResult = await CreateMethodWriter(enabled: true).ReplaceMethodBodyAsync(
            location.RelativePath, location.Line, location.Column, _usageSnapshotId,
            _readBody!.ContentHash, body, Token);
    }

    [When(@"I attempt to replace its body with ""(.*)""")]
    public async Task WhenAttemptReplace(string body) =>
        _methodError = await Record.ExceptionAsync(() => WhenReplaceBody(body));

    [When("I attempt to replace its body with writing disabled")]
    public async Task WhenAttemptDisabledEdit()
    {
        var location = _usageTarget!.Location;
        _methodError = await Record.ExceptionAsync(() =>
            CreateMethodWriter(enabled: false).ReplaceMethodBodyAsync(
                location.RelativePath, location.Line, location.Column, _usageSnapshotId,
                _readBody!.ContentHash, "{ return 2; }", Token));
    }

    [When("I attempt to read the selected method body again")]
    public async Task WhenAttemptReadAgain() =>
        _methodError = await Record.ExceptionAsync(WhenReadBody);

    [Then(@"the method body request fails with ""(.*)""")]
    public void ThenMethodRequestFails(string message)
    {
        Assert.NotNull(_methodError);
        Assert.Contains(message, _methodError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Then("the method body replacement succeeds and requires reindexing")]
    public void ThenWriteSucceeded()
    {
        Assert.NotNull(_writeResult);
        Assert.True(_writeResult.ReindexRequired);
        Assert.NotEqual(_readBody!.ContentHash, _writeResult.ContentHash);
        Assert.Equal(_usageTarget!.Location, _writeResult.MethodLocation);
    }

    [Then(@"the source file contains ""(.*)""")]
    public void ThenSourceContains(string content)
    {
        var path = Path.Combine(_root, _usageTarget!.Location.RelativePath);
        Assert.Contains(content, File.ReadAllText(path));
    }

    private RepositoryMethodTools CreateMethodReader() => new(MethodConfiguration(), CreateMethodOperations(enabled: false));

    private RepositoryWriteTools CreateMethodWriter(bool enabled) => new(MethodConfiguration(), CreateMethodOperations(enabled));

    private MethodBodyOperations CreateMethodOperations(bool enabled) =>
        new(_stateProvider, Store, new RepositorySourceReader(), new RepositorySourceWriter(),
            new CSharpMethodBodyEditor(), new CSharpMethodDeclarationEditor(),
            new CSharpMethodSyntaxDiagnostics(), new RepositoryWriteAccess(enabled));

    private IConfiguration MethodConfiguration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["CODESEMANTIC4LLM_REPOSITORY_ROOT"] = _root })
        .Build();
}
