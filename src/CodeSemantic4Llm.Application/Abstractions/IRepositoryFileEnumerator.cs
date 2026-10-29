namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Enumerates repository-relative files without entering excluded or linked directories.</summary>
public interface IRepositoryFileEnumerator
{
    IEnumerable<string> EnumerateFiles(
        string repositoryRoot,
        IReadOnlyList<string> exclusionPatterns,
        CancellationToken cancellationToken = default);
}
