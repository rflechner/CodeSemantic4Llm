using System.Security.Cryptography;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Source;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Exercises syntax-only diagnostics via the actual indexed method and local checkout.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private MethodDiagnosticsResult? _syntaxReport;
    private Exception? _syntaxFailure;
    private readonly List<MethodSyntaxDiagnostic> _allSyntaxDiagnostics = [];
    private int _syntaxPageCount;

    [When(@"I validate proposed method body ""(.*)""")]
    public async Task WhenValidateProposedBody(string newBody)
    {
        var location = _usageTarget!.Location;
        _syntaxReport = await CreateMethodReader().ValidateMethodBodyAsync(
            location.RelativePath, location.Line, location.Column, _usageSnapshotId,
            _readBody!.ContentHash, newBody, cancellationToken: Token);
        _allSyntaxDiagnostics.Clear();
        _allSyntaxDiagnostics.AddRange(_syntaxReport.Diagnostics);
    }

    [When(@"I attempt to validate proposed method body ""(.*)""")]
    public async Task WhenAttemptValidation(string body) =>
        _syntaxFailure = await Record.ExceptionAsync(() => WhenValidateProposedBody(body));

    [When("I fetch the current method syntax diagnostics")]
    public async Task WhenGetCurrentDiagnostics()
    {
        var location = _usageTarget!.Location;
        _syntaxReport = await CreateMethodReader().GetMethodDiagnosticsAsync(
            location.RelativePath, location.Line, location.Column, _usageTarget.Name,
            cancellationToken: Token);
        _allSyntaxDiagnostics.Clear();
        _allSyntaxDiagnostics.AddRange(_syntaxReport.Diagnostics);
    }

    [When(@"I attempt to fetch method diagnostics named ""(.*)""")]
    public async Task WhenGetWrongDiagnostics(string methodName)
    {
        var location = _usageTarget!.Location;
        _syntaxFailure = await Record.ExceptionAsync(() =>
            CreateMethodReader().GetMethodDiagnosticsAsync(
                location.RelativePath, location.Line, location.Column, methodName,
                cancellationToken: Token));
    }

    [When(@"I read current method diagnostics with page size (\d+)")]
    public async Task WhenPageDiagnostics(int maxDiagnostics)
    {
        var location = _usageTarget!.Location;
        var offset = 0;
        _syntaxPageCount = 0;
        _allSyntaxDiagnostics.Clear();

        do
        {
            _syntaxReport = await CreateMethodReader().GetMethodDiagnosticsAsync(
                location.RelativePath, location.Line, location.Column, _usageTarget.Name,
                maxDiagnostics, offset, cancellationToken: Token);
            _allSyntaxDiagnostics.AddRange(_syntaxReport.Diagnostics);
            _syntaxPageCount++;

            if (_syntaxReport.NextOffset is not int next)
            {
                break;
            }

            offset = next;
        } while (true);
    }

    [Then("method syntax diagnostics are valid")]
    public void ThenSyntaxIsValid()
    {
        Assert.NotNull(_syntaxReport);
        Assert.True(_syntaxReport.IsValid);
        Assert.Equal(0, _syntaxReport.TotalDiagnostics);
        Assert.Empty(_allSyntaxDiagnostics);
    }

    [Then("method syntax diagnostics are invalid")]
    public void ThenSyntaxIsInvalid()
    {
        Assert.NotNull(_syntaxReport);
        Assert.False(_syntaxReport.IsValid);
        Assert.True(_syntaxReport.TotalDiagnostics > 0);
    }

    [Then(@"method syntax diagnostics include ""(.*)""")]
    public void ThenSyntaxIncludesId(string diagnosticId)
    {
        Assert.Contains(_allSyntaxDiagnostics, diagnostic =>
            diagnostic.Id == diagnosticId && diagnostic.Severity == "error");
        Assert.All(_allSyntaxDiagnostics, diagnostic =>
        {
            Assert.True(diagnostic.Location.Line > 0);
            Assert.True(diagnostic.Location.Column > 0);
        });
    }

    [Then("the diagnostics refer to the updated source hash")]
    public void ThenDiagnosticHashIsCurrent()
    {
        var path = Path.Combine(_root, _usageTarget!.Location.RelativePath);
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

        Assert.NotNull(_syntaxReport);
        Assert.Equal(hash, _syntaxReport.ContentHash);

        if (_readBody is not null)
        {
            Assert.NotEqual(_readBody.ContentHash, hash);
        }
    }

    [Then("method syntax diagnostics span multiple pages")]
    public void ThenDiagnosticsPaginated()
    {
        Assert.True(_syntaxPageCount >= 2);
        Assert.NotNull(_syntaxReport);
        Assert.False(_syntaxReport.HasMore);
        Assert.Equal(_syntaxReport.TotalDiagnostics, _allSyntaxDiagnostics.Count);
    }

    [Then(@"every diagnostic belongs to ""(.*)""")]
    public void ThenDiagnosticLocations(string path)
    {
        Assert.All(_allSyntaxDiagnostics, diagnostic =>
            Assert.Equal(path, diagnostic.Location.RelativePath));
    }

    [Then(@"the syntax diagnostics request fails with ""(.*)""")]
    public void ThenSyntaxRequestFails(string message)
    {
        Assert.NotNull(_syntaxFailure);
        Assert.Contains(message, _syntaxFailure.Message, StringComparison.OrdinalIgnoreCase);
    }
}
