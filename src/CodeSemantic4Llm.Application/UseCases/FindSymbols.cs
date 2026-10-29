using CodeSemantic4Llm.Application.Abstractions;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Queries the index through a bounded, repository- and branch-scoped use case.</summary>
public sealed class FindSymbols(IIndexStore indexStore, IRepositoryStateProvider stateProvider)
{
    public async Task<SymbolSearchResult> ExecuteAsync(
        string repositoryRoot,
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

        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var result = indexStore.FindSymbols(state.Scope, name, maxResults, cancellationToken);

        if (result.Snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("HEAD has changed since this index was recorded. Run index again before searching.");
        }

        return result;
    }
}
