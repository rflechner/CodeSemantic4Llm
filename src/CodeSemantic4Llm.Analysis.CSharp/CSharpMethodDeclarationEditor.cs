using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>Replaces one Roslyn method declaration, including its attributes and signature.</summary>
public sealed class CSharpMethodDeclarationEditor : IMethodDeclarationEditor
{
    private const string DeclarationInputHelp =
        "Supply exactly one C# method declaration: attributes if needed, signature and body "
        + "(or a declaration ending in ';'). Do not include a class, namespace, other members or surrounding comments.";

    public string Read(SourceDocument document, SourceLocation declaration)
    {
        var method = CSharpMethodBodyEditor.FindMethod(document, declaration);
        var lineStart = method.SyntaxTree.GetText().Lines.GetLineFromPosition(method.SpanStart).Start;
        var prefix = document.Content.AsSpan(lineStart, method.SpanStart - lineStart);
        var start = prefix.IndexOfAnyExcept(' ', '\t') < 0 ? lineStart : method.SpanStart;
        return document.Content[start..method.Span.End];
    }

    public MethodDeclarationEdit Replace(SourceDocument document, SourceLocation declaration, string newMethod)
    {
        ArgumentNullException.ThrowIfNull(newMethod);
        var method = CSharpMethodBodyEditor.FindMethod(document, declaration);
        var replacement = CSharpMethodBodyEditor.TrimBlankLines(newMethod);
        var parsed = SyntaxFactory.ParseMemberDeclaration(replacement, consumeFullText: true);
        var newLine = document.Content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (parsed is MethodDeclarationSyntax recoveredMethod)
        {
            // Roslyn recovers method structure even when a string or expression is malformed.
            // Normalize first so error positions describe the text that would be written.
            replacement = CSharpMethodIndentation.AdjustDeclaration(method, recoveredMethod, replacement, newLine);
            parsed = SyntaxFactory.ParseMemberDeclaration(replacement, consumeFullText: true);
        }

        // Span includes attributes but excludes documentation comments and boundary trivia.
        var content = string.Concat(document.Content.AsSpan(0, method.SpanStart),
            replacement, document.Content.AsSpan(method.Span.End));
        var proposedText = SourceText.From(content);
        var validated = ValidateDeclaration(parsed, replacement, proposedText, document.RelativePath, method.SpanStart);
        var position = proposedText.Lines.GetLinePosition(method.SpanStart + validated.Identifier.SpanStart);
        return new MethodDeclarationEdit(content,
            new SourceLocation(document.RelativePath, position.Line + 1, position.Character + 1));
    }

    private static MethodDeclarationSyntax ValidateDeclaration(
        MemberDeclarationSyntax? parsed, string replacement, SourceText proposedText, string relativePath, int start)
    {
        var errors = CSharpSyntaxDiagnosticReader.ReadReplacementErrors(
            parsed?.GetDiagnostics() ?? [], relativePath, proposedText, start).ToList();

        if (parsed is not MethodDeclarationSyntax method
            || method.FullSpan.Length != replacement.Length
            || method.GetLeadingTrivia().Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            || method.GetTrailingTrivia().Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia)))
        {
            var position = proposedText.Lines.GetLinePosition(start);
            errors.Add(new SyntaxDiagnostic("CS4L0002", "error", DeclarationInputHelp,
                new SourceLocation(relativePath, position.Line + 1, position.Character + 1)));
        }

        if (errors.Count > 0)
        {
            throw new MethodEditValidationException(errors);
        }

        return (MethodDeclarationSyntax)parsed!;
    }
}
