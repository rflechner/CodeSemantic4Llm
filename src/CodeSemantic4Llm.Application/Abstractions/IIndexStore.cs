using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Persistence port. Every operation is scoped to a repository and branch.
/// Implementations store derived index metadata, never source-file contents.
/// </summary>
public interface IIndexStore
{
    Task ReplaceFileAsync(
        RepositoryScope scope,
        string relativePath,
        string contentHash,
        IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SymbolDefinition>> FindSymbolsAsync(
        RepositoryScope scope,
        string name,
        int maxResults,
        CancellationToken cancellationToken = default);
}
