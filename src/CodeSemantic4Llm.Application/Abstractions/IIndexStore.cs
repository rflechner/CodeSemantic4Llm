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

    /// <summary>
    /// Returns declarations ranked by FTS5/BM25 across names and generated summaries.
    /// The published snapshot and branch are enforced; source files are never read.
    /// </summary>
    SymbolSearchResult SearchCode(
        RepositoryScope scope, string query, string? kind, int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Matches simple, full or abbreviated qualified names using DeclarationSelector's source-address rules.
    /// Returns up to two candidates so callers can reject ambiguous declarations.
    /// </summary>
    IReadOnlyList<IndexedDeclaration> SelectDeclarations(
        RepositoryScope scope, DeclarationSelector selector, bool typesOnly,
        CancellationToken cancellationToken = default);

    /// <summary>Returns semantically resolved occurrences of one indexed declaration.</summary>
    SymbolUsagePage FindUsages(
        RepositoryScope scope, SourceLocation targetLocation, long snapshotId,
        int maxResults, int offset, CancellationToken cancellationToken = default);

    /// <summary>Finds one exact indexed method and its source-file hash in a published snapshot.</summary>
    IndexedMethod GetIndexedMethod(
        RepositoryScope scope, SourceLocation methodLocation, long snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists direct members of one type declaration in the expected published snapshot.</summary>
    TypeMemberPage GetTypeMembers(
        RepositoryScope scope, SourceLocation typeLocation, long snapshotId,
        string? kind, int maxResults, int offset, CancellationToken cancellationToken = default);
}

/// <summary>A bounded declaration search with the identity of the saved snapshot and truncation information.</summary>
public sealed record SymbolSearchResult(
    RepositoryIndexSnapshot Snapshot, IReadOnlyList<SymbolDefinition> Symbols, bool HasMore);

/// <summary>One page of direct members. Partial declarations and inherited members are not combined.</summary>
public sealed record TypeMemberPage(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Type, IReadOnlyList<SymbolDefinition> Members, int? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}


/// <summary>One page of references to an exact type or method; results contain locations only.</summary>
public sealed record SymbolUsagePage(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Target, IReadOnlyList<SourceLocation> Usages, int? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}

/// <summary>One method declaration with the exact original bytes' hash, scoped to a branch snapshot.</summary>
public sealed record IndexedMethod(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Method, string ContentHash);

/// <summary>A declaration and its source provenance, used internally for navigation.</summary>
public sealed record IndexedDeclaration(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Symbol, string ContentHash);
