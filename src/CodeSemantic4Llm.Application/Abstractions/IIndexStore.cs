using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Persistence port. Every operation is scoped to a repository and branch.
/// Implementations store derived index metadata, never source-file contents.
/// </summary>
public interface IIndexStore
{
    /// <summary>Starts an isolated generation. Configuration changes prevent reuse from the previous generation.</summary>
    IIndexUpdate BeginUpdate(
        RepositoryState state, string configurationFingerprint, string semanticLanguage,
        CancellationToken cancellationToken = default);

    /// <summary>Returns published metadata only; a missing index is never created by a read.</summary>
    RepositoryIndexSnapshot? GetSnapshot(RepositoryScope scope, CancellationToken cancellationToken = default);

    /// <summary>Queries a single published snapshot, in deterministic location order.</summary>
    SymbolSearchResult FindSymbols(
        RepositoryScope scope,
        string name,
        int maxResults,
        CancellationToken cancellationToken = default);
}

/// <summary>A bounded declaration search with the identity of the saved snapshot and truncation information.</summary>
public sealed record SymbolSearchResult(
    RepositoryIndexSnapshot Snapshot, IReadOnlyList<SymbolDefinition> Symbols, bool HasMore);
