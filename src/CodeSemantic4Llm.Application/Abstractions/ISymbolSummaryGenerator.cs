using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Generates a short natural-language description of a source declaration without persisting its code.</summary>
public interface ISymbolSummaryGenerator
{
    Task<string> SummarizeAsync(
        SymbolDefinition symbol, string declarationSource,
        SemanticIndexingConfiguration configuration, CancellationToken cancellationToken = default);
}
