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

    internal static MethodDeclarationSyntax FindMethod(
        SourceDocument document, SourceLocation declaration, string? expectedMethodName = null)
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
                return position.Line + 1 == declaration.Line && position.Character + 1 == declaration.Column
                    && (expectedMethodName is null || candidate.Identifier.ValueText == expectedMethodName);
            });

        return method ?? throw new InvalidOperationException(
            "No C# method exists at the selected indexed declaration location. Run index again.");
    }

    internal static TextSpan GetBodySpan(MethodDeclarationSyntax method)
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
        var (body, hasValidShape, syntaxErrors) = InspectReplacement(newBody);

        if (!hasValidShape)
        {
            throw new ArgumentException(
                "Supply a complete C# block enclosed in braces, for example '{ return 42; }'.",
                nameof(newBody));
        }

        if (syntaxErrors.Count > 0)
        {
            throw new ArgumentException(
                "The replacement must be one syntactically valid C# method body.", nameof(newBody));
        }

        return body;
    }

    /// <summary>
    /// Shares the exact syntax gate between replace_method_body and validate_method_body,
    /// so preflight cannot approve a block that the writer's syntax gate would reject.
    /// </summary>
    internal static (string Body, bool HasValidShape, IReadOnlyList<Diagnostic> SyntaxErrors)
        InspectReplacement(string newBody)
    {
        ArgumentNullException.ThrowIfNull(newBody);
        var body = newBody.Trim();

        var statement = SyntaxFactory.ParseStatement(body);
        var isCompleteBlock = body.Length >= 2 && body[0] == '{' && body[^1] == '}'
            && statement is BlockSyntax && statement.FullSpan.Length == body.Length;

        return (body, isCompleteBlock,
            statement.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray());
    }
}
