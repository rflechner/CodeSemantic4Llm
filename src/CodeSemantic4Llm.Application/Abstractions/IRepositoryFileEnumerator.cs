namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Recursively enumerates repository-relative files in deterministic directory order, skipping
/// excluded paths and symbolic links. The application selects files by analyzer extension rules.
/// </summary>
public interface IRepositoryFileEnumerator
{
    IEnumerable<string> EnumerateFiles(
        string repositoryRoot,
        IReadOnlyList<string> exclusionPatterns,
        CancellationToken cancellationToken = default);
}
