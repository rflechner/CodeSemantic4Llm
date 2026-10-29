namespace CodeSemantic4Llm.Domain.Repositories;

/// <summary>Observed checkout identity and HEAD commit. A revision does not prove the working tree is clean.</summary>
public sealed record RepositoryState(RepositoryScope Scope, string? Revision);
