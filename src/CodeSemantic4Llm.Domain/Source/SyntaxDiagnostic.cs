using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Domain.Source;

/// <summary>A syntax diagnostic located in the original file or a proposed in-memory edit.</summary>
public sealed record SyntaxDiagnostic(
    string Id,
    string Severity,
    string Message,
    SourceLocation Location);
