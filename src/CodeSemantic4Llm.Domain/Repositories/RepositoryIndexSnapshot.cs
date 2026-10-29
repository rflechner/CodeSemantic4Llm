namespace CodeSemantic4Llm.Domain.Repositories;

/// <summary>A completely published working-tree index. RecordedRevision is context, not a freshness guarantee.</summary>
public sealed record RepositoryIndexSnapshot(
    RepositoryScope Scope,
    long SnapshotId,
    string? RecordedRevision,
    string SemanticLanguage,
    DateTimeOffset CompletedAt,
    int FileCount,
    int SymbolCount);
