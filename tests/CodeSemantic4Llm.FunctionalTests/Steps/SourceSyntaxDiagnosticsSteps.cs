using System.Security.Cryptography;
using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Tools;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Checks current disk diagnostics through the MCP boundary with real files and YAML.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private SourceDiagnosticsResult? _sourceDiagnostics;
    private Exception? _sourceDiagnosticsFailure;
    private readonly List<SyntaxDiagnostic> _sourceDiagnosticPages = [];
    private int _sourceDiagnosticPageCount;
    private byte[]? _sourceBeforeDiagnostics;

    [When(@"I fetch current file diagnostics for ""(.*)""")]
    public async Task WhenGetFileDiagnostics(string path)
    {
        _sourceBeforeDiagnostics = File.ReadAllBytes(Path.Combine(_root, path));
        _sourceDiagnostics = await CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(path, cancellationToken: Token);
        _sourceDiagnosticPages.Clear();
        _sourceDiagnosticPages.AddRange(_sourceDiagnostics.Diagnostics);
    }

    [When(@"I attempt current file diagnostics for ""(.*)""")]
    public async Task WhenAttemptFileDiagnostics(string path) =>
        _sourceDiagnosticsFailure = await Record.ExceptionAsync(() =>
            CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(path, cancellationToken: Token));

    [When(@"I fetch current type diagnostics for ""(.*)"" in ""(.*)"" at line (\d+) column (\d+)")]
    public async Task WhenGetTypeDiagnostics(string name, string path, int line, int column)
    {
        _sourceDiagnostics = await CreateSourceDiagnosticsTools().GetTypeDiagnosticsAsync(
            path, line, column, name, cancellationToken: Token);
        _sourceDiagnosticPages.Clear();
        _sourceDiagnosticPages.AddRange(_sourceDiagnostics.Diagnostics);
    }

    [When(@"I attempt current type diagnostics for ""(.*)"" in ""(.*)"" at line (\d+) column (\d+)")]
    public async Task WhenAttemptTypeDiagnostics(string name, string path, int line, int column) =>
        _sourceDiagnosticsFailure = await Record.ExceptionAsync(() => WhenGetTypeDiagnostics(name, path, line, column));

    [When(@"I read all current file diagnostics for ""(.*)"" with page size (\d+)")]
    public async Task WhenPageFileDiagnostics(string path, int pageSize)
    {
        _sourceDiagnosticPages.Clear();
        _sourceDiagnosticPageCount = 0;
        int? nextOffset = 0;
        string? hash = null;

        while (nextOffset is int offset)
        {
            _sourceDiagnostics = await CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(
                path, pageSize, offset, hash, Token);
            Assert.True(_sourceDiagnostics.Diagnostics.Count <= pageSize);
            _sourceDiagnosticPages.AddRange(_sourceDiagnostics.Diagnostics);
            _sourceDiagnosticPageCount++;
            nextOffset = _sourceDiagnostics.NextOffset;
            hash = _sourceDiagnostics.ContentHash;
        }
    }

    [When(@"I fetch the first diagnostic page for ""(.*)""")]
    public async Task WhenGetFirstSourceDiagnosticPage(string path) =>
        _sourceDiagnostics = await CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(
            path, maxDiagnostics: 1, cancellationToken: Token);

    [When(@"I attempt current file diagnostics for ""(.*)"" with limit (-?\d+) and offset (-?\d+) without a hash")]
    public async Task WhenAttemptUnpinnedSourceDiagnosticPage(string path, int limit, int offset) =>
        _sourceDiagnosticsFailure = await Record.ExceptionAsync(() =>
            CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(path, limit, offset, cancellationToken: Token));

    [Then("the source diagnostic page only contains a warning but the file is invalid")]
    public void ThenSourceValidityIncludesOtherPages()
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.Equal("warning", Assert.Single(_sourceDiagnostics.Diagnostics).Severity);
        Assert.False(_sourceDiagnostics.IsValid);
        Assert.True(_sourceDiagnostics.HasMore);
        Assert.True(_sourceDiagnostics.TotalDiagnostics > 1);
    }

    [When("I attempt the next current source diagnostic page")]
    public async Task WhenAttemptNextSourceDiagnosticPage()
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.NotNull(_sourceDiagnostics.NextOffset);
        _sourceDiagnosticsFailure = await Record.ExceptionAsync(() =>
            CreateSourceDiagnosticsTools().GetFileDiagnosticsAsync(
                _sourceDiagnostics.RelativePath, 1, _sourceDiagnostics.NextOffset.Value,
                _sourceDiagnostics.ContentHash, Token));
    }

    [Then(@"current source diagnostics include error ""(.*)""")]
    public void ThenSourceDiagnosticError(string id)
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.False(_sourceDiagnostics.IsValid);
        Assert.Contains(_sourceDiagnosticPages, diagnostic => diagnostic.Id == id && diagnostic.Severity == "error");
    }

    [Then(@"current source syntax is valid with warning ""(.*)""")]
    public void ThenSourceDiagnosticWarning(string id)
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.True(_sourceDiagnostics.IsValid);
        var diagnostic = Assert.Single(_sourceDiagnostics.Diagnostics);
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal("warning", diagnostic.Severity);
    }

    [Then("current source diagnostics identify the bytes on disk")]
    public void ThenSourceDiagnosticHash()
    {
        Assert.NotNull(_sourceDiagnostics);
        var bytes = File.ReadAllBytes(Path.Combine(_root, _sourceDiagnostics.RelativePath));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), _sourceDiagnostics.ContentHash);
    }

    [Then("reading source diagnostics leaves the file unchanged")]
    public void ThenDiagnosticsReadOnly()
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.Equal(_sourceBeforeDiagnostics, File.ReadAllBytes(Path.Combine(_root, _sourceDiagnostics.RelativePath)));
    }

    [Then(@"current source diagnostics only refer to line (\d+)")]
    public void ThenSourceDiagnosticLine(int line) =>
        Assert.All(_sourceDiagnosticPages, diagnostic => Assert.Equal(line, diagnostic.Location.Line));

    [Then(@"current source diagnostics contain (\d+) errors across (\d+) pages")]
    public void ThenSourceDiagnosticPagination(int errors, int pages)
    {
        Assert.NotNull(_sourceDiagnostics);
        Assert.Equal(pages, _sourceDiagnosticPageCount);
        Assert.Equal(errors, _sourceDiagnosticPages.Count);
        Assert.Equal(errors, _sourceDiagnostics.TotalDiagnostics);
        Assert.False(_sourceDiagnostics.HasMore);
        Assert.Equal(_sourceDiagnosticPages.OrderBy(diagnostic => diagnostic.Location.Line)
            .ThenBy(diagnostic => diagnostic.Location.Column).ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal),
            _sourceDiagnosticPages);
    }

    [Then(@"the current source diagnostic request fails with ""(.*)""")]
    public void ThenSourceDiagnosticFailure(string message)
    {
        Assert.NotNull(_sourceDiagnosticsFailure);
        Assert.Contains(message, _sourceDiagnosticsFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    private RepositoryDiagnosticsTools CreateSourceDiagnosticsTools() => new(MethodConfiguration(),
        new GetSourceDiagnostics(new RepositorySourceReader(), new YamlRepositoryConfigurationLoader(),
            [new CSharpSourceAnalyzer()], [new CSharpSourceSyntaxDiagnostics()]));
}
