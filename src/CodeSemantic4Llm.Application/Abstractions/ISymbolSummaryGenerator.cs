using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Generates a short natural-language description of a source declaration without persisting its code.</summary>
public interface ISymbolSummaryGenerator
{
    /// <summary>Returns a generated description, or an empty string when no text summary is available.</summary>
    /// <remarks>An empty result leaves the symbol's existing summary unchanged and does not interrupt indexing.</remarks>
    Task<string> SummarizeAsync(
        SymbolDefinition symbol, string declarationSource,
        SemanticIndexingConfiguration configuration, CancellationToken cancellationToken = default);
}
