using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

public sealed class DeclarationSelectionTests
{
    [Theory]
    [InlineData("../Worker.cs")]
    [InlineData("/Worker.cs")]
    [InlineData("src//Worker.cs")]
    [InlineData("C:\\Worker.cs")]
    public void Rejects_paths_outside_the_checkout(string path) =>
        Assert.Throws<ArgumentException>(() => new DeclarationSelector(path, "Run"));

    [Fact]
    public void Selection_matches_names_and_positions_without_guessing()
    {
        var method = Method(3);
        Assert.True(new DeclarationSelector("Worker.cs", "Run").Matches(method));
        Assert.True(new DeclarationSelector("Worker.cs", "Worker.Run", 3, 16).Matches(method));
        Assert.False(new DeclarationSelector("Worker.cs", "Run", 4).Matches(method));
        Assert.False(new DeclarationSelector("Worker.cs", "run").Matches(method));
        Assert.False(new DeclarationSelector("Other.cs", "Run").Matches(method));
        Assert.Throws<ArgumentException>(() => new DeclarationSelector("Worker.cs", "Run", column: 16));
    }

    [Theory]
    [InlineData("RunAsync", true)]
    [InlineData("CliApplication.RunAsync", true)]
    [InlineData("Host.CliApplication.RunAsync", true)]
    [InlineData("Cli.Host.CliApplication.RunAsync", true)]
    [InlineData("TinyHarness.Cli.Host.CliApplication.RunAsync", true)]
    [InlineData("Application.RunAsync", false)]
    [InlineData("Other.CliApplication.RunAsync", false)]
    [InlineData("cliApplication.RunAsync", false)]
    [InlineData("CliApplication.Run", false)]
    [InlineData("CliApplication.RunAsync()", false)]
    [InlineData("CliApplication.RunAsyn_", false)]
    [InlineData("CliApplication.Run%", false)]
    public void Qualified_names_match_only_complete_case_sensitive_suffixes(string name, bool matches)
    {
        var method = new SymbolDefinition("csharp", "method", "RunAsync",
            "TinyHarness.Cli.Host.CliApplication.RunAsync", new SourceLocation("CliApplication.cs", 7, 26));

        Assert.Equal(matches, new DeclarationSelector("CliApplication.cs", name).Matches(method));
        Assert.False(new DeclarationSelector("Other.cs", name).Matches(method));
        Assert.False(new DeclarationSelector("CliApplication.cs", name, 8, 26).Matches(method));
        Assert.False(new DeclarationSelector("CliApplication.cs", name, 7, 27).Matches(method));
    }

    [Fact]
    public void Read_history_requires_a_read_and_is_scoped_to_the_checkout()
    {
        var history = new MethodReadHistory();
        var state = new RepositoryState(new RepositoryScope(Path.GetTempPath(), "main"), "revision");
        var observation = new MethodReadObservation(state, Method(3), "hash");
        history.Remember(observation);
        Assert.Equal(observation, history.Get(state.Scope.RootPath, observation.Method.Location));
        Assert.Throws<CodeRequestException>(() => history.Get(Path.Combine(Path.GetTempPath(), "other"), observation.Method.Location));
        Assert.Throws<CodeRequestException>(() => new MethodReadHistory().Get(state.Scope.RootPath, observation.Method.Location));
    }

    private static SymbolDefinition Method(int line) => new(
        "csharp", "method", "Run", "Worker.Run", new SourceLocation("Worker.cs", line, 16));
}
