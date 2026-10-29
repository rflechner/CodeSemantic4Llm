namespace CodeSemantic4Llm.Domain.Symbols;

/// <summary>
/// Language-independent fields shared by semantic declarations.
/// Language-specific data must remain representable in specialized models.
/// </summary>
public sealed record SymbolDefinition(
    string Language,
    string Kind,
    string Name,
    string QualifiedName,
    SourceLocation Location);
