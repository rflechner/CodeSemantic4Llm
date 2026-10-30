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
    /// </summary>
    string Replace(SourceDocument document, SourceLocation declaration, string newBody);
}
