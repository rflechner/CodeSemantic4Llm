using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Syntax-aware reading and replacement of a complete method declaration.</summary>
public interface IMethodDeclarationEditor
{
    /// <summary>Reads attributes, signature and implementation with local indentation, excluding surrounding comments.</summary>
    string Read(SourceDocument document, SourceLocation declaration);

    /// <summary>
    /// Replaces exactly one declaration and returns its new identifier location. Surrounding source is
    /// preserved; references are not rewritten and syntax validation does not prove compilation succeeds.
    /// </summary>
    /// <exception cref="MethodEditValidationException">
    /// The proposed declaration is invalid. Diagnostics refer to its normalized positions in the proposed file.
    /// </exception>
    MethodDeclarationEdit Replace(SourceDocument document, SourceLocation declaration, string newMethod);
}
