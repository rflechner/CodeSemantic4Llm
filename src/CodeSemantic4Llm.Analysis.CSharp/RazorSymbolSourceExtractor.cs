using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Supplies transient Razor file and C# member snippets to the optional LLM summary generator.
/// The source code is not saved to the SQLite index.
/// </summary>
public sealed class RazorSymbolSourceExtractor : ISymbolSourceExtractor
{
    public string AnalyzerId => "dotnet-razor";

    public IReadOnlyDictionary<SourceLocation, string> Extract(
        SourceDocument document, IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(symbols);

        var requested = symbols
            .Where(symbol => symbol.Kind is "component" or "view" or "method" or "constructor"
                or "class" or "struct" or "interface" or "record")
            .Select(symbol => symbol.Location)
            .ToHashSet();

        if (requested.Count == 0)
        {
            return new Dictionary<SourceLocation, string>();
        }

        return RazorDocumentParser.Parse(document, cancellationToken)
            .Where(declaration => declaration.Source is not null && requested.Contains(declaration.Symbol.Location))
            .ToDictionary(declaration => declaration.Symbol.Location, declaration => declaration.Source!);
    }
}
