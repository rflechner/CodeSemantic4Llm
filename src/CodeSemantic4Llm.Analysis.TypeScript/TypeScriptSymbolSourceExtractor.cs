using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Analysis.TypeScript;

/// <summary>Extracts transient TypeScript declarations and their leading JSDoc for optional LLM summarization.</summary>
public sealed class TypeScriptSymbolSourceExtractor : ISymbolSourceExtractor
{
    public string AnalyzerId => "typescript";

    public IReadOnlyDictionary<SourceLocation, string> Extract(
        SourceDocument document, IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        var requested = symbols.Select(symbol => symbol.Location).ToHashSet();

        if (requested.Count == 0)
        {
            return new Dictionary<SourceLocation, string>();
        }

        return TypeScriptSyntaxTree.Read(document, root =>
        {
            var result = new Dictionary<SourceLocation, string>();

            foreach (var declaration in TypeScriptDeclarations.Read(document, root, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!requested.Contains(declaration.Symbol.Location))
                {
                    continue;
                }

                var leading = TypeScriptDeclarations.LeadingDocumentation(declaration.Syntax);
                result.Add(declaration.Symbol.Location,
                    leading is null ? declaration.Syntax.Text : leading.Text + "\n" + declaration.Syntax.Text);
            }

            return (IReadOnlyDictionary<SourceLocation, string>)result;
        }, cancellationToken);
    }
}
