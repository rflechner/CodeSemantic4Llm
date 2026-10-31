namespace CodeSemantic4Llm.Domain.Symbols;

/// <summary>
/// Language-independent fields shared by semantic declarations.
/// Language-specific data must remain representable in specialized models.
/// ContainingType identifies the immediate declaring type in the same source file;
/// it does not infer inheritance or merge partial declarations across files.
/// Summary contains readable source documentation when available, or an optional generated description.
/// </summary>
public sealed record SymbolDefinition(
    string Language,
    string Kind,
    string Name,
    string QualifiedName,
    SourceLocation Location,
    SourceLocation? ContainingType = null,
    string? Summary = null);
