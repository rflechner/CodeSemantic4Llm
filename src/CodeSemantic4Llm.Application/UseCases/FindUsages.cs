using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Lists saved, semantically resolved references to one exact declaration.</summary>
public sealed class FindUsages(IIndexStore indexStore, IRepositoryStateProvider stateProvider)
{
    public async Task<SymbolUsagePage> ExecuteAsync(
        string repositoryRoot, SourceLocation targetLocation, long snapshotId,
        int maxResults = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetLocation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (maxResults > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Limit must not exceed 200.");
        }

        var relativePath = targetLocation.RelativePath;
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':')
            || relativePath.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Use the repository-relative path returned by find_symbols.", nameof(targetLocation));
        }

        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var result = indexStore.FindUsages(
            state.Scope, targetLocation, snapshotId, maxResults, offset, cancellationToken);

        if (result.Snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("HEAD has changed since this index was recorded. Run index again before searching.");
        }

        return result;
    }
}
