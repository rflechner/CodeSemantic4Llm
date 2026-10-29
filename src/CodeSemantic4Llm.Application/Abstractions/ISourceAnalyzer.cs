using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Language-specific parsing and declaration extraction contract.</summary>
public interface ISourceAnalyzer
{
    string LanguageId { get; }

    bool Supports(string relativePath);

    Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default);
}
