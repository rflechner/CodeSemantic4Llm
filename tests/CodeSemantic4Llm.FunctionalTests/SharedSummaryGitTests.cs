using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Runs the actual CLI merge driver through Git, including rebase's reversed side roles.</summary>
public sealed class SharedSummaryGitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Merge_and_rebase_can_keep_the_upstream_complete_document(bool rebase)
    {
        using var repo = new SharedSummaryTestRepository();
        await PrepareDivergedBranches(repo);
        var policy = rebase ? "current" : "other";
        var driver = Driver(policy);
        if (rebase)
        {
            await repo.GitAsync("-c", "merge.codesemantic-summary.driver=" + driver, "rebase", "--merge", "upstream");
        }
        else
        {
            await repo.GitAsync("-c", "merge.codesemantic-summary.driver=" + driver, "merge", "upstream", "--no-edit");
        }

        Assert.Contains("upstreamquasar", File.ReadAllText(repo.SummaryPath));
        Assert.DoesNotContain("localpulsar", File.ReadAllText(repo.SummaryPath));
        Assert.Empty(await repo.GitAsync("diff", "--name-only", "--diff-filter=U"));
        repo.Codec.Decode(File.ReadAllText(repo.SummaryPath));
    }

    [Fact]
    public async Task Dirty_tracked_summaries_are_not_overwritten_by_the_driver()
    {
        using var repo = new SharedSummaryTestRepository();
        await PrepareDivergedBranches(repo);
        var dirty = File.ReadAllText(repo.SummaryPath).Replace("localpulsar", "uncommittededit");
        File.WriteAllText(repo.SummaryPath, dirty);
        var result = await repo.RunGitAsync("-c", "merge.codesemantic-summary.driver=" + Driver("other"), "merge", "upstream", "--no-edit");
        Assert.NotEqual(0, result.Code);
        Assert.Equal(dirty, File.ReadAllText(repo.SummaryPath));
    }

    [Fact]
    public async Task Source_conflicts_still_require_resolution_and_suspend_the_watcher()
    {
        using var repo = new SharedSummaryTestRepository();
        await PrepareDivergedBranches(repo, conflictInSource: true);
        var result = await repo.RunGitAsync("-c", "merge.codesemantic-summary.driver=" + Driver("other"), "merge", "upstream", "--no-edit");
        Assert.NotEqual(0, result.Code);
        Assert.Contains("Sample.cs", await repo.GitAsync("diff", "--name-only", "--diff-filter=U"));
        Assert.True(await new GitSummaryIntegration().IsOperationInProgressAsync(repo.Root, repo.Token));
        Assert.Contains("upstreamquasar", File.ReadAllText(repo.SummaryPath));
        await repo.GitAsync("merge", "--abort");
        Assert.False(await new GitSummaryIntegration().IsOperationInProgressAsync(repo.Root, repo.Token));
    }

    [Fact]
    public async Task Untracked_local_summaries_block_an_incoming_tracked_file()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        await new GitSummaryIntegration().SetupAsync(repo.Root, repo.Token);
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Source only");
        await repo.GitAsync("switch", "-c", "upstream");
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        var markdown = File.ReadAllText(repo.SummaryPath);
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Publish summaries");
        await repo.GitAsync("switch", "main");
        Directory.CreateDirectory(Path.GetDirectoryName(repo.SummaryPath)!);
        File.WriteAllText(repo.SummaryPath, markdown.Replace("nebula", "untrackededit"));
        var result = await repo.RunGitAsync("-c", "merge.codesemantic-summary.driver=" + Driver("other"), "merge", "upstream");
        Assert.NotEqual(0, result.Code);
        Assert.Contains("untrackededit", File.ReadAllText(repo.SummaryPath));
    }

    [Fact]
    public async Task Setup_is_idempotent_and_limited_to_repository_configuration()
    {
        using var repo = new SharedSummaryTestRepository();
        await repo.InitializeAsync();
        File.WriteAllText(Path.Combine(repo.Root, ".gitattributes"), "*.cs text eol=lf\n");
        var git = new GitSummaryIntegration();
        await git.SetupAsync(repo.Root, repo.Token);
        await git.SetupAsync(repo.Root, repo.Token);
        var attributes = File.ReadAllText(Path.Combine(repo.Root, ".gitattributes"));
        Assert.Contains("*.cs text eol=lf", attributes);
        Assert.Equal(1, attributes.Split('\n').Count(line => line == GitSummaryIntegration.AttributeRule));
        Assert.Equal(GitSummaryIntegration.OtherDriver, (await repo.GitAsync("config", "--local", "merge.codesemantic-summary.driver")).Trim());
        Assert.Empty(await repo.GitAsync("diff", "--cached", "--name-only"));
    }

    private static string Driver(string policy)
    {
        var assembly = typeof(CommandLineApplication).Assembly.Location.Replace('\\', '/');
        return $"dotnet \"{assembly}\" git-merge-summary {policy} \"%O\" \"%A\" \"%B\"";
    }

    private static async Task PrepareDivergedBranches(SharedSummaryTestRepository repo, bool conflictInSource = false)
    {
        await repo.InitializeAsync();
        await repo.Indexer.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await repo.Exporter.ExecuteAsync(repo.Root, cancellationToken: repo.Token);
        await new GitSummaryIntegration().SetupAsync(repo.Root, repo.Token);
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Base summaries");
        await repo.GitAsync("switch", "-c", "upstream");
        File.WriteAllText(repo.SummaryPath, File.ReadAllText(repo.SummaryPath).Replace("nebula", "upstreamquasar"));
        if (conflictInSource) File.WriteAllText(Path.Combine(repo.Root, "Sample.cs"), "class Upstream {}\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Upstream summaries");
        await repo.GitAsync("switch", "main");
        File.WriteAllText(repo.SummaryPath, File.ReadAllText(repo.SummaryPath).Replace("nebula", "localpulsar"));
        if (conflictInSource) File.WriteAllText(Path.Combine(repo.Root, "Sample.cs"), "class Local {}\n");
        // Keep a separate local change so rebase does not drop the entire replayed commit.
        File.WriteAllText(Path.Combine(repo.Root, "Local.txt"), "local change\n");
        await repo.GitAsync("add", ".");
        await repo.GitAsync("commit", "-m", "Local summaries");
    }
}
