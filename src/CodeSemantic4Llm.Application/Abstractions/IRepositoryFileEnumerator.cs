namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Recursively enumerates repository-relative files in deterministic directory order, skipping
/// excluded paths and symbolic links. The application selects files by analyzer extension rules.
/// </summary>
public interface IRepositoryFileEnumerator
{
    /// <summary>Optionally restricts traversal to a directory relative to the checkout root.</summary>
    IEnumerable<string> EnumerateFiles(
        string repositoryRoot,
        IReadOnlyList<string> exclusionPatterns,
        CancellationToken cancellationToken = default,
        string? folder = null);
}
