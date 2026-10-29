using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Reads one source file inside a repository boundary, rejecting symbolic links and traversal.</summary>
public interface IRepositorySourceReader
{
    Task<SourceFileSnapshot> ReadAsync(
        string repositoryRoot, string relativePath, CancellationToken cancellationToken = default);
}
