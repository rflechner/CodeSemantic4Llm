using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Selects the appropriate language analyzer without depending on its implementation.</summary>
public sealed class AnalyzeSource(IEnumerable<ISourceAnalyzer> analyzers)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    public Task<IReadOnlyList<SymbolDefinition>> ExecuteAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default)
    {
        var analyzer = _analyzers.FirstOrDefault(candidate => candidate.Supports(document.RelativePath));

        if (analyzer is null)
        {
            throw new NotSupportedException($"No analyzer is registered for '{document.RelativePath}'.");
        }

        return analyzer.AnalyzeAsync(document, cancellationToken);
    }
}
