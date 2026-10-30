using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Parses current source text for diagnostics without compiling or executing a project.</summary>
public interface ISourceSyntaxDiagnostics
{
    /// <summary>The configured source analyzer whose language this adapter supports.</summary>
    string AnalyzerId { get; }

    /// <summary>Reports all syntax errors and warnings, including those outside declarations.</summary>
    IReadOnlyList<SyntaxDiagnostic> GetFile(
        SourceDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports diagnostics within one exact type declaration, including nested declarations.
    /// Other partial declarations are not included. Both identifier position and name must match.
    /// </summary>
    IReadOnlyList<SyntaxDiagnostic> GetType(
        SourceDocument document, SourceLocation declaration, string expectedTypeName,
        CancellationToken cancellationToken = default);
}
