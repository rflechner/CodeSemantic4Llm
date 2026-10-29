namespace CodeSemantic4Llm.Domain.Source;

/// <summary>A transient decoded document and SHA-256 hash of the exact bytes read from the checkout.</summary>
public sealed record SourceFileSnapshot(SourceDocument Document, string ContentHash);
