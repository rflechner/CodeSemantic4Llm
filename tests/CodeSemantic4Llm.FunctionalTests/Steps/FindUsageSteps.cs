using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Symbols;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>End-to-end reference navigation through the MCP tool and the real SQLite index.</summary>
public sealed partial class RepositoryIndexingSteps
{
    private SymbolDefinition? _usageTarget;
    private long _usageSnapshotId;
    private SymbolUsagePage? _usagePage;
    private readonly List<SourceLocation> _usageLocations = [];
    private int _usagePageSize;
    private Exception? _usageFailure;

    [Given(@"a method with (\d+) call sites in the indexing checkout")]
    public void GivenManyCalls(int count)
    {
        WriteSource("Worker.cs", "class Worker { public void Run() {} }");
        WriteSource("Caller.cs", "class Caller { void Use(Worker worker) {\n" +
            string.Join("\n", Enumerable.Range(0, count).Select(_ => "worker.Run();")) + "\n} }");
    }

    [When(@"I select indexed ""(.*)"" declaration number (\d+)")]
    public async Task WhenSelectUsageTarget(string name, int number)
    {
        var result = await CreateMcpTools().FindSymbolsAsync(name, 200, Token);
        var candidates = result.Symbols.Where(symbol => symbol.Kind is "method" or "class" or "struct" or "interface" or "record" or "enum" or "delegate")
            .ToArray();
        _usageTarget = candidates[number - 1];
        _usageSnapshotId = result.Snapshot.SnapshotId;
    }

    [When(@"I find its usages with page size (\d+)")]
    public async Task WhenFindUsages(int pageSize)
    {
        _usagePageSize = pageSize;
        _usageLocations.Clear();
        _usagePage = await ReadUsagesAsync(_usageTarget!.Location, pageSize, 0);
        Assert.Equal(_usageTarget, _usagePage.Target);
        _usageLocations.AddRange(_usagePage.Usages);
    }

    [When("I read all remaining usage pages")]
    public async Task WhenReadUsagePages()
    {
        while (_usagePage!.NextOffset is int offset)
        {
            Assert.Equal(_usageLocations.Count, offset);
            _usagePage = await ReadUsagesAsync(_usageTarget!.Location, _usagePageSize, offset);
            _usageLocations.AddRange(_usagePage.Usages);
        }
    }

    [When("I attempt to find its usages")]
    public async Task WhenAttemptUsageQuery() => _usageFailure = await Record.ExceptionAsync(
        () => ReadUsagesAsync(_usageTarget!.Location, 50, 0));

    [When("I request usages with \"(.*)\"")]
    public async Task WhenRequestInvalidUsages(string problem)
    {
        var location = _usageTarget!.Location;
        var limit = 50;
        var offset = 0;

        switch (problem)
        {
            case "excessive limit": limit = 201; break;
            case "negative offset": offset = -1; break;
            case "parent traversal": location = new SourceLocation("../Worker.cs", 1, 1); break;
            case "missing location": location = new SourceLocation(location.RelativePath, 999, 1); break;
            default: throw new ArgumentException(problem);
        }

        _usageFailure = await Record.ExceptionAsync(() => ReadUsagesAsync(location, limit, offset));
    }

    [Then("more usages are available")]
    public void ThenMoreUsages()
    {
        Assert.NotNull(_usagePage);
        Assert.True(_usagePage.HasMore);
        Assert.Equal(_usagePageSize, _usagePage.Usages.Count);
    }

    [Then(@"the usage listing contains (\d+) references without another page")]
    public void ThenUsageCount(int count)
    {
        Assert.Equal(count, _usageLocations.Count);
        Assert.NotNull(_usagePage);
        Assert.False(_usagePage.HasMore);
        Assert.Null(_usagePage.NextOffset);
        Assert.Equal(_usageSnapshotId, _usagePage.Snapshot.SnapshotId);
        Assert.Equal(_usageLocations.Count, _usageLocations.Distinct().Count());
    }

    [Then("all usages point to \"(.*)\"")]
    public void ThenAllUsagesPointTo(string path) =>
        Assert.All(_usageLocations, location => Assert.Equal(path, location.RelativePath));

    [Then("the usage request fails with \"(.*)\"")]
    public void ThenUsageFails(string message)
    {
        Assert.NotNull(_usageFailure);
        Assert.Contains(message, _usageFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Given("the cache uses the previous version-two schema")]
    public void GivenVersionTwoSchema() => ExecuteSql("""
        DROP TABLE symbol_references;
        ALTER TABLE snapshots DROP COLUMN reference_index_version;
        PRAGMA user_version = 2;
        """);

    private Task<SymbolUsagePage> ReadUsagesAsync(SourceLocation location, int limit, int offset) =>
        CreateMcpTools().FindUsagesAsync(location.RelativePath, location.Line, location.Column, _usageSnapshotId,
            limit, offset, Token);
}
