using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Language-specific, syntax-aware selection and replacement of one method body.</summary>
public interface IMethodBodyEditor
{
    /// <summary>Returns precisely the body of a declaration identified by its source location.</summary>
    MethodBody Read(SourceDocument document, SourceLocation declaration);

    /// <summary>
    /// Replaces only the selected body's source span, leaving the surrounding source unchanged.
    /// C# accepts a complete block or its inner statements; omitted outer braces are added before syntax checking.
    /// Replacement indentation is rebased to the declaration while preserving relative inner indentation
    /// and significant whitespace in literals. Code line endings follow the source document.
    /// </summary>
    string Replace(SourceDocument document, SourceLocation declaration, string newBody);
}
