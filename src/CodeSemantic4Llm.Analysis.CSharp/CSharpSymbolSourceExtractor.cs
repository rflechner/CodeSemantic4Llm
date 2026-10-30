using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Uses declaration identity from Roslyn to extract complete types and methods, including leading
/// XML documentation trivia. No declaration source is written to the index database.
/// </summary>
public sealed class CSharpSymbolSourceExtractor : ISymbolSourceExtractor
{
    public string AnalyzerId => "dotnet-csharp";

    public IReadOnlyDictionary<SourceLocation, string> Extract(
        SourceDocument document, IReadOnlyList<SymbolDefinition> symbols,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(symbols);

        var requested = symbols
            .Where(symbol => symbol.Kind is "class" or "struct" or "interface" or "record" or "method" or "constructor")
            .Select(symbol => symbol.Location)
            .ToHashSet();
        var result = new Dictionary<SourceLocation, string>();

        if (requested.Count == 0)
        {
            return result;
        }

        var root = CSharpSyntaxTree.ParseText(document.Content, cancellationToken: cancellationToken)
            .GetRoot(cancellationToken);

        foreach (var member in root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            SyntaxToken? identifier = member switch
            {
                TypeDeclarationSyntax type => type.Identifier,
                MethodDeclarationSyntax method => method.Identifier,
                ConstructorDeclarationSyntax constructor => constructor.Identifier,
                _ => null
            };

            if (identifier is null)
            {
                continue;
            }

            var position = identifier.Value.GetLocation().GetLineSpan().StartLinePosition;
            var location = new SourceLocation(document.RelativePath, position.Line + 1, position.Character + 1);

            if (requested.Contains(location))
            {
                // ToFullString includes XML <summary>, <param>, etc. from leading trivia.
                // A type node includes its entire body, including nested declarations.
                result.Add(location, member.ToFullString());
            }
        }

        return result;
    }
}
