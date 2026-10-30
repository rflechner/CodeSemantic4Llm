using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Symbols;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Member-navigation scenarios share the real Git/SQLite fixture with repository indexing.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private SymbolDefinition? _selectedType;
    private long _selectedSnapshotId;
    private TypeMemberPage? _memberPage;
    private readonly List<SymbolDefinition> _listedMembers = [];
    private string? _memberKind;
    private int _memberPageSize;
    private Exception? _memberFailure;

    [Given("indexing source \"(.*)\" is:")]
    public void GivenIndexingSource(string path, string source) => WriteSource(path, source);

    [Given(@"a class with (\d+) methods in ""(.*)""")]
    public void GivenManyMethods(int count, string path) =>
        WriteSource(path, "class Large {\n" + string.Join("\n", Enumerable.Range(0, count)
            .Select(index => $"public void Method{index:D4}() {{}}")) + "\n}");

    [When("I find type \"(.*)\" in \"(.*)\"")]
    public async Task WhenFindType(string name, string path)
    {
        var result = await CreateMcpTools().FindSymbolsAsync(name, 200, TestContext.Current.CancellationToken);
        _selectedType = Assert.Single(result.Symbols, symbol => symbol.Location.RelativePath == path
            && symbol.Kind is "class" or "struct" or "interface" or "record" or "enum");
        _selectedSnapshotId = result.Snapshot.SnapshotId;
    }

    [When(@"I list its ""(.*)"" members with page size (\d+)")]
    public async Task WhenListMembers(string kind, int pageSize)
    {
        _memberKind = kind == "all" ? null : kind;
        _memberPageSize = pageSize;
        _listedMembers.Clear();
        _memberPage = await ReadMemberPageAsync(_selectedType!.Location, _memberKind, pageSize, 0);
        _listedMembers.AddRange(_memberPage.Members);
    }

    [When("I read the remaining type member pages")]
    public async Task WhenReadRemainingPages()
    {
        while (_memberPage!.NextOffset is int offset)
        {
            Assert.Equal(_listedMembers.Count, offset);
            _memberPage = await ReadMemberPageAsync(_selectedType!.Location, _memberKind, _memberPageSize, offset);
            _listedMembers.AddRange(_memberPage.Members);
        }
    }

    [When("I attempt to list its methods")]
    public async Task WhenAttemptMemberQuery() => _memberFailure = await Record.ExceptionAsync(
        () => ReadMemberPageAsync(_selectedType!.Location, "method", 50, 0));

    [When("I request members with \"(.*)\"")]
    public async Task WhenInvalidMemberQuery(string problem)
    {
        var location = _selectedType!.Location;
        var kind = "method";
        var limit = 50;
        var offset = 0;

        switch (problem)
        {
            case "unknown kind": kind = "methods"; break;
            case "excessive limit": limit = 201; break;
            case "negative offset": offset = -1; break;
            case "parent traversal": location = new SourceLocation("../Worker.cs", 1, 1); break;
            case "missing location": location = new SourceLocation(location.RelativePath, 500, 1); break;
            case "method location":
                location = Assert.Single((await CreateMcpTools().FindSymbolsAsync("Run", cancellationToken: Token)).Symbols).Location;
                break;
            default: throw new ArgumentException(problem);
        }

        _memberFailure = await Record.ExceptionAsync(() => ReadMemberPageAsync(location, kind, limit, offset));
    }

    [Then("more type members are available")]
    public void ThenMoreMembers()
    {
        Assert.NotNull(_memberPage);
        Assert.True(_memberPage.HasMore);
        Assert.Equal(_memberPageSize, _memberPage.Members.Count);
        Assert.Equal(_memberPageSize, _memberPage.NextOffset);
    }

    [Then("the listed member names are:")]
    public void ThenMemberNames(Table table) =>
        Assert.Equal(table.Rows.Select(row => row["name"]), _listedMembers.Select(member => member.Name));

    [Then("every listed member belongs to the selected type declaration")]
    public void ThenMemberOwnership()
    {
        Assert.NotNull(_memberPage);
        Assert.Equal(_selectedType, _memberPage.Type);
        Assert.Equal(_selectedSnapshotId, _memberPage.Snapshot.SnapshotId);
        Assert.All(_listedMembers, member => Assert.Equal(_selectedType!.Location, member.ContainingType));
        Assert.Equal(_listedMembers.Count, _listedMembers.Select(member => member.Location).Distinct().Count());
    }

    [Then(@"the member listing contains (\d+) declarations without another page")]
    public void ThenMemberCount(int count)
    {
        Assert.Equal(count, _listedMembers.Count);
        Assert.NotNull(_memberPage);
        Assert.False(_memberPage.HasMore);
        Assert.Null(_memberPage.NextOffset);
    }

    [Then("the member request fails with \"(.*)\"")]
    public void ThenMemberFailure(string message)
    {
        Assert.NotNull(_memberFailure);
        Assert.Contains(message, _memberFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Given("the cache uses the original version-one schema")]
    public void GivenOriginalSchema() => ExecuteSql("""
        DROP TABLE symbol_references;
        ALTER TABLE snapshots DROP COLUMN reference_index_version;
        DROP INDEX ix_symbols_container;
        ALTER TABLE symbols DROP COLUMN containing_type_line;
        ALTER TABLE symbols DROP COLUMN containing_type_column;
        ALTER TABLE snapshots DROP COLUMN member_index_version;
        PRAGMA user_version = 1;
        """);

    [Then(@"the cache schema version is (\d+)")]
    public void ThenCacheVersion(int version)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal((long)version, command.ExecuteScalar());
    }

    private Task<TypeMemberPage> ReadMemberPageAsync(SourceLocation location, string? kind, int limit, int offset) =>
        CreateMcpTools().GetTypeMembersAsync(location.RelativePath, location.Line, location.Column, _selectedSnapshotId,
            kind, limit, offset, TestContext.Current.CancellationToken);
}
