using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Language-specific syntax diagnostics; never compiles or executes source code.</summary>
public interface IMethodSyntaxDiagnostics
{
    /// <summary>
    /// Reports syntax diagnostics belonging to the selected method in the current source text.
    /// The method name must match to avoid silently selecting a different declaration.
    /// </summary>
    IReadOnlyList<SyntaxDiagnostic> GetCurrent(
        SourceDocument document, SourceLocation declaration, string expectedMethodName);

    /// <summary>
    /// Reports diagnostics for a proposed replacement of one method body, without writing files.
    /// Uses the editor's normalization rules; locations refer to the proposed file after normalization.
    /// </summary>
    IReadOnlyList<SyntaxDiagnostic> ValidateReplacement(
        SourceDocument document, SourceLocation declaration, string newBody);
}
