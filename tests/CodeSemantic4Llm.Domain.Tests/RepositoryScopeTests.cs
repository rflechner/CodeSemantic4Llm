using CodeSemantic4Llm.Domain.Repositories;
using Xunit;

namespace CodeSemantic4Llm.Domain.Tests;

public sealed class RepositoryScopeTests
{
    [Fact]
    public void Constructor_normalizes_root_and_preserves_branch_name()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo", "..", "repo");
        var scope = new RepositoryScope(root, "feature/my-index");

        Assert.Equal(Path.GetFullPath(root), scope.RootPath);
        Assert.Equal("feature/my-index", scope.BranchName);
    }

    [Fact]
    public void Constructor_rejects_empty_branch_name()
    {
        Assert.Throws<ArgumentException>(() => new RepositoryScope(".", " "));
    }
}
