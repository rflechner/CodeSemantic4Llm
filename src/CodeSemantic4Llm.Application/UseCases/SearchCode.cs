using CodeSemantic4Llm.Application.Abstractions;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Queries the published branch's full-text index without accessing code or calling an LLM.</summary>
public sealed class SearchCode(IIndexStore indexStore, IRepositoryStateProvider stateProvider)
{
    public async Task<SymbolSearchResult> ExecuteAsync(
        string repositoryRoot, string query, string? kind = null, int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        if (maxResults > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Limit must not exceed 200.");
        }

        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var result = indexStore.SearchCode(state.Scope, query, kind, maxResults, cancellationToken);

        if (result.Snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("HEAD has changed since this index was recorded. Run index again before searching.");
        }

        return result;
    }
}
