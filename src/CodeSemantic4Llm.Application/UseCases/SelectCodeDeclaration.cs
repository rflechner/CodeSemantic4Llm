using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Resolves an unambiguous source address for navigation and checks its recorded provenance.</summary>
public sealed class SelectCodeDeclaration(
    IIndexStore store, IRepositoryStateProvider stateProvider, IRepositorySourceReader sourceReader)
{
    public async Task<IndexedDeclaration> ExecuteAsync(
        string root, DeclarationSelector selector, bool typesOnly, CancellationToken cancellationToken)
    {
        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        var candidates = store.SelectDeclarations(state.Scope, selector, typesOnly, cancellationToken);

        if (candidates.Count == 0)
        {
            throw new CodeRequestException("No matching declaration. Check the file, name and source position.");
        }

        if (candidates.Count > 1)
        {
            throw new CodeRequestException("Several declarations match. Specify the identifier's line and column.");
        }

        var selected = candidates[0];
        var source = await sourceReader.ReadAsync(root, selector.RelativePath, cancellationToken);

        if (state.Revision != selected.Snapshot.RecordedRevision || source.ContentHash != selected.ContentHash)
        {
            throw new CodeRequestException("Navigation is unavailable for the changed source. Ask the server operator to refresh it.");
        }

        return selected;
    }
}
