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
    internal const string BodyInputHelp = "Supply only C# statements or one complete body block, with balanced braces. "
        + "Do not include the method signature; use replace_method for a complete declaration or signature changes. "
        + "Wrap an intentional single local function in outer body braces.";

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
        var replacement = ValidateBody(document, method, newBody);

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
            "No C# method exists at the selected indexed declaration location. Run index again, or use get_file_diagnostics to check current syntax without selecting a declaration.");
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

    private static string ValidateBody(SourceDocument document, MethodDeclarationSyntax method, string newBody)
    {
        var (body, hasValidShape, syntaxErrors) = InspectReplacement(document, method, newBody);

        if (!hasValidShape)
        {
            throw new ArgumentException(
                BodyInputHelp,
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
    /// Shares normalization and the syntax gate between validation and editing. A leading block
    /// is treated as explicit; otherwise statements are enclosed without repairing unmatched braces.
    /// </summary>
    internal static (string Body, bool HasValidShape, IReadOnlyList<Diagnostic> SyntaxErrors)
        InspectReplacement(SourceDocument document, MethodDeclarationSyntax method, string newBody)
    {
        ArgumentNullException.ThrowIfNull(newBody);
        var body = TrimBlankLines(newBody);
        var newLine = document.Content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var statement = SyntaxFactory.ParseStatement(body);

        if (statement is not BlockSyntax)
        {
            // A complete method is also a legal local-function statement. Do not silently nest
            // an accidentally supplied declaration inside the method that the caller meant to edit.
            if (SyntaxFactory.ParseMemberDeclaration(body, consumeFullText: true) is MethodDeclarationSyntax member
                && !member.ContainsDiagnostics)
            {
                return (body, false, []);
            }

            // Separate the closing brace from trailing // comments and preprocessor directives.
            body = "{" + newLine + body + newLine + "}";
            statement = SyntaxFactory.ParseStatement(body);
        }
        else if (statement.Span.End < body.Length)
        {
            // A supplied trailing line comment must not consume source after the replaced span.
            body += newLine;
            statement = SyntaxFactory.ParseStatement(body);
        }

        var isCompleteBlock = statement is BlockSyntax block
            && !block.OpenBraceToken.IsMissing && !block.CloseBraceToken.IsMissing
            && statement.FullSpan.Length == body.Length;

        if (isCompleteBlock)
        {
            body = CSharpMethodIndentation.AdjustBody(method, (BlockSyntax)statement, body, newLine);
            statement = SyntaxFactory.ParseStatement(body);
        }

        return (body, isCompleteBlock,
            statement.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToArray());
    }

    internal static string TrimBlankLines(string body)
    {
        var trimmed = body.TrimEnd();
        var firstContent = 0;
        while (firstContent < trimmed.Length && char.IsWhiteSpace(trimmed[firstContent]))
        {
            firstContent++;
        }

        // Keep horizontal indentation on the first content line so it participates in rebasing.
        var start = firstContent;
        while (start > 0 && trimmed[start - 1] is not '\r' and not '\n')
        {
            start--;
        }

        return trimmed[start..];
    }
}
