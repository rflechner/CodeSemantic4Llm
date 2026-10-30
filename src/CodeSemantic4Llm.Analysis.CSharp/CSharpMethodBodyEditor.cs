using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>Uses Roslyn to select a precise C# method and replace only its executable body.</summary>
public sealed class CSharpMethodBodyEditor : IMethodBodyEditor
{
    public MethodBody Read(SourceDocument document, SourceLocation declaration)
    {
        var method = FindMethod(document, declaration);
        var span = GetBodySpan(method);
        var text = document.Content.Substring(span.Start, span.Length);
        return new MethodBody(text, method.ExpressionBody is not null);
    }

    public string Replace(SourceDocument document, SourceLocation declaration, string newBody)
    {
        ArgumentNullException.ThrowIfNull(newBody);
        var method = FindMethod(document, declaration);
        var span = GetBodySpan(method);
        var replacement = ValidateBody(newBody);

        // Replace a TextSpan, not the whole Roslyn node: preserve the signature, attributes,
        // comments and byte-for-byte source outside the targeted body (including CRLF).
        var content = document.Content;
        return string.Concat(content.AsSpan(0, span.Start), replacement, content.AsSpan(span.End));
    }

    private static MethodDeclarationSyntax FindMethod(SourceDocument document, SourceLocation declaration)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(declaration);

        if (!string.Equals(document.RelativePath, declaration.RelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("The method declaration belongs to another source file.", nameof(declaration));
        }

        var tree = CSharpSyntaxTree.ParseText(document.Content, path: document.RelativePath);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .SingleOrDefault(candidate =>
            {
                var position = candidate.Identifier.GetLocation().GetLineSpan().StartLinePosition;
                return position.Line + 1 == declaration.Line && position.Character + 1 == declaration.Column;
            });

        return method ?? throw new InvalidOperationException(
            "No C# method exists at the selected indexed declaration location. Run index again.");
    }

    private static TextSpan GetBodySpan(MethodDeclarationSyntax method)
    {
        if (method.Body is { } block)
        {
            return block.Span;
        }

        if (method.ExpressionBody is { } expression && method.SemicolonToken.RawKind != 0)
        {
            return TextSpan.FromBounds(expression.SpanStart, method.SemicolonToken.Span.End);
        }

        throw new InvalidOperationException(
            "The selected method has no executable body (for example, an interface or abstract declaration).");
    }

    private static string ValidateBody(string newBody)
    {
        var body = newBody.Trim();

        if (body.Length < 2 || body[0] != '{' || body[^1] != '}')
        {
            throw new ArgumentException("Supply a complete C# block enclosed in braces, for example '{ return 42; }'.", nameof(newBody));
        }

        // Parse a single block rather than accepting an entire method or declarations that
        // could escape the selected body. No target code is compiled or executed.
        var statement = SyntaxFactory.ParseStatement(body);

        if (statement is not BlockSyntax || statement.FullSpan.Length != body.Length ||
            statement.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new ArgumentException("The replacement must be one syntactically valid C# method body.", nameof(newBody));
        }

        return body;
    }
}
