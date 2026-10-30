using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>Checks C# files and exact type declarations using Roslyn's recoverable syntax tree.</summary>
public sealed class CSharpSourceSyntaxDiagnostics : ISourceSyntaxDiagnostics
{
    public string AnalyzerId => "dotnet-csharp";

    public IReadOnlyList<SyntaxDiagnostic> GetFile(
        SourceDocument document, CancellationToken cancellationToken = default)
    {
        var tree = Parse(document, cancellationToken);
        return CSharpSyntaxDiagnosticReader.Read(tree, cancellationToken: cancellationToken);
    }

    public IReadOnlyList<SyntaxDiagnostic> GetType(
        SourceDocument document, SourceLocation declaration, string expectedTypeName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTypeName);

        if (!string.Equals(document.RelativePath, declaration.RelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The type declaration belongs to another source file.", nameof(declaration));
        }

        var tree = Parse(document, cancellationToken);
        var type = FindType(tree.GetRoot(cancellationToken), declaration, expectedTypeName, cancellationToken);
        return CSharpSyntaxDiagnosticReader.Read(tree, type.Span, cancellationToken);
    }

    private static SyntaxTree Parse(SourceDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        return CSharpSyntaxTree.ParseText(document.Content, path: document.RelativePath,
            cancellationToken: cancellationToken);
    }

    private static SyntaxNode FindType(
        SyntaxNode root, SourceLocation declaration, string expectedTypeName, CancellationToken cancellationToken)
    {
        foreach (var node in root.DescendantNodes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var identifier = node switch
            {
                BaseTypeDeclarationSyntax type => type.Identifier,
                DelegateDeclarationSyntax type => type.Identifier,
                _ => default
            };

            if (identifier.RawKind == 0 || identifier.IsMissing || identifier.ValueText != expectedTypeName)
            {
                continue;
            }

            var position = identifier.GetLocation().GetLineSpan().StartLinePosition;

            if (position.Line + 1 == declaration.Line && position.Character + 1 == declaration.Column)
            {
                return node;
            }
        }

        throw new InvalidOperationException(
            "No C# type exists at the selected declaration location with the expected name. " +
            "Use get_file_diagnostics if edits moved or damaged the declaration, or refresh its location.");
    }
}
