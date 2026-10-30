using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Domain.Source;

/// <summary>A transient source edit and the identifier location of the replacement declaration.</summary>
public sealed record MethodDeclarationEdit(string Content, SourceLocation MethodLocation);
