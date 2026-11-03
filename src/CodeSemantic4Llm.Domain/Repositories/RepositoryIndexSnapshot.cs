namespace CodeSemantic4Llm.Domain.Repositories;

/// <summary>A published branch snapshot. Partial snapshots cover selected folders only; HEAD is not a freshness guarantee.</summary>
public sealed record RepositoryIndexSnapshot(
    RepositoryScope Scope,
    long SnapshotId,
    string? RecordedRevision,
    string SemanticLanguage,
    DateTimeOffset CompletedAt,
    int FileCount,
    int SymbolCount,
    bool IsPartial = false);
