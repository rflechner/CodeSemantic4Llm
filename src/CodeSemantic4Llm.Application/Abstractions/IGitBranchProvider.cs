namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Resolves the actual checked-out branch; does not change the working tree.</summary>
public interface IGitBranchProvider
{
    ValueTask<string> GetCurrentBranchAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}
