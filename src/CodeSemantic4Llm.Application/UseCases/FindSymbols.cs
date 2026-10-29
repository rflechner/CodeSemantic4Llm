using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Queries the index through a bounded, repository- and branch-scoped use case.</summary>
public sealed class FindSymbols(IIndexStore indexStore)
{
    public Task<IReadOnlyList<SymbolDefinition>> ExecuteAsync(
        RepositoryScope scope,
        string name,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        if (maxResults > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Limit must not exceed 200.");
        }

        return indexStore.FindSymbolsAsync(scope, name, maxResults, cancellationToken);
    }
}
