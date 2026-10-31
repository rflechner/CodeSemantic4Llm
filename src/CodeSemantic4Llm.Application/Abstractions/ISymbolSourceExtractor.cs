using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Extracts transient declaration source, including documentation trivia, for supported symbol kinds.
/// Callers must never persist these snippets in the index.
/// </summary>
public interface ISymbolSourceExtractor
{
    string AnalyzerId { get; }

    IReadOnlyDictionary<SourceLocation, string> Extract(
        SourceDocument document, IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken cancellationToken = default);
}
