namespace CodeSemantic4Llm.Domain.Symbols;

/// <summary>
/// A resolved occurrence of an indexed declaration. Both locations are repository-relative;
/// this relationship contains no source text or Roslyn-specific identity.
/// </summary>
public sealed record SymbolReference(SourceLocation Target, SourceLocation Usage);
