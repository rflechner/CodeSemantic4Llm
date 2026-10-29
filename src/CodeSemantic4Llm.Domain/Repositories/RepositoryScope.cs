namespace CodeSemantic4Llm.Domain.Repositories;

/// <summary>
/// Identifies the source checkout and Git branch whose index is being read or updated.
/// </summary>
public sealed record RepositoryScope
{
    public RepositoryScope(string rootPath, string branchName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(branchName);

        RootPath = Path.GetFullPath(rootPath);
        BranchName = branchName;
    }

    /// <summary>The absolute location of the original checkout, not a copy of its source files.</summary>
    public string RootPath { get; }

    /// <summary>The Git branch name, stored as data rather than used directly as a directory name.</summary>
    public string BranchName { get; }
}
