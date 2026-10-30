using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Rebases body indentation without formatting expressions or changing whitespace inside literals.
/// The existing declaration supplies the base indentation and its body or ancestors supply the indent step.
/// </summary>
internal static class CSharpMethodIndentation
{
    public static string AdjustBody(MethodDeclarationSyntax method, BlockSyntax block, string body, string newLine)
    {
        var source = method.SyntaxTree.GetText();
        var baseIndent = GetIndentation(source, method.GetFirstToken().SpanStart);
        var contentIndent = baseIndent + FindIndentStep(source, method, baseIndent);
        var lines = ReadLines(block, SourceText.From(body),
            TextSpan.FromBounds(block.OpenBraceToken.Span.End, block.CloseBraceToken.SpanStart));
        return AdjustLines(block, body, lines, baseIndent, contentIndent, newLine);
    }

    public static string AdjustDeclaration(
        MethodDeclarationSyntax method, MethodDeclarationSyntax replacement, string text, string newLine)
    {
        var baseIndent = GetIndentation(method.SyntaxTree.GetText(), method.GetFirstToken().SpanStart);
        var lines = ReadLines(replacement, SourceText.From(text), replacement.FullSpan);
        return AdjustLines(replacement, text, lines, baseIndent, baseIndent, newLine);
    }

    private static string AdjustLines(SyntaxNode syntax, string body, IReadOnlyList<BodyLine> lines,
        string baseIndent, string contentIndent, string newLine)
    {
        var commonIndent = FindCommonIndentation(lines);
        var result = new StringBuilder(body.Length);

        foreach (var line in lines)
        {
            AppendLine(result, line, baseIndent, contentIndent, commonIndent);

            if (line.Text.EndIncludingLineBreak > line.Text.End)
            {
                result.Append(IsProtectedPosition(syntax, line.Text.End)
                    ? body[line.Text.End..line.Text.EndIncludingLineBreak]
                    : newLine);
            }
        }

        return result.ToString();
    }

    private static void AppendLine(
        StringBuilder result, BodyLine line, string baseIndent, string contentIndent, string commonIndent)
    {
        if (line.IsProtected || line.IsDirective)
        {
            result.Append(line.Text.ToString());
            return;
        }

        if (line.IsBlank)
        {
            return;
        }

        // The untouched source prefix already supplies the first line's indentation.
        if (line.Text.LineNumber > 0)
        {
            result.Append(line.IsInsideBlock ? contentIndent : baseIndent);

            if (line.IsInsideBlock)
            {
                result.Append(line.Indentation.AsSpan(commonIndent.Length));
            }
        }

        result.Append(line.Text.ToString().AsSpan(line.Indentation.Length));
    }

    private static IReadOnlyList<BodyLine> ReadLines(SyntaxNode syntax, SourceText text, TextSpan indentedSpan) =>
        text.Lines.Select(line =>
        {
            var indentation = GetIndentation(text, line.Start);
            var contentStart = line.Start + indentation.Length;
            return new BodyLine(
                line, indentation,
                indentedSpan.Contains(contentStart),
                IsProtectedPosition(syntax, line.Start),
                contentStart < line.End && text[contentStart] == '#');
        }).ToArray();

    private static string FindCommonIndentation(IReadOnlyList<BodyLine> lines)
    {
        string? common = null;

        foreach (var line in lines.Where(line =>
            line.IsInsideBlock && !line.IsBlank && !line.IsProtected && !line.IsDirective))
        {
            if (common is null)
            {
                common = line.Indentation;
                continue;
            }

            var length = 0;
            while (length < common.Length && length < line.Indentation.Length
                && common[length] == line.Indentation[length])
            {
                length++;
            }

            common = common[..length];
        }

        return common ?? "";
    }

    private static string FindIndentStep(SourceText source, MethodDeclarationSyntax method, string baseIndent)
    {
        foreach (var statement in method.Body?.Statements ?? default)
        {
            var indent = GetOwnLineIndentation(source, statement.GetFirstToken().SpanStart);
            if (indent is not null && indent.StartsWith(baseIndent, StringComparison.Ordinal)
                && indent.Length > baseIndent.Length)
            {
                return indent[baseIndent.Length..];
            }
        }

        // Empty and expression-bodied methods have no statement indentation to copy.
        // An enclosing type/namespace still provides evidence of the local style.
        foreach (var ancestor in method.Ancestors())
        {
            var indent = GetOwnLineIndentation(source, ancestor.GetFirstToken().SpanStart);
            if (indent is not null && baseIndent.StartsWith(indent, StringComparison.Ordinal)
                && baseIndent.Length > indent.Length)
            {
                return baseIndent[indent.Length..];
            }
        }

        return baseIndent.Contains('\t') ? "\t" : "    ";
    }

    private static string? GetOwnLineIndentation(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);
        var indent = GetIndentation(text, position);
        return line.Start + indent.Length == position ? indent : null;
    }

    private static string GetIndentation(SourceText text, int position)
    {
        var line = text.Lines.GetLineFromPosition(position);
        var end = line.Start;
        while (end < line.End && text[end] is ' ' or '\t')
        {
            end++;
        }

        return text.ToString(TextSpan.FromBounds(line.Start, end));
    }

    private static bool IsProtectedPosition(SyntaxNode syntax, int position)
    {
        if (position >= syntax.FullSpan.End)
        {
            return false;
        }

        // Literal tokens and whole interpolated strings can contain meaningful whitespace,
        // including raw-string closing delimiters. Disabled code cannot safely be reindented.
        var token = syntax.FindToken(position);
        if (token.SpanStart < position && token.Span.Contains(position))
        {
            return true;
        }

        if (token.Parent?.AncestorsAndSelf().OfType<InterpolatedStringExpressionSyntax>()
            .Any(expression => expression.SpanStart < position && expression.Span.Contains(position)) == true)
        {
            return true;
        }

        var trivia = syntax.FindTrivia(position);
        return trivia.IsKind(SyntaxKind.DisabledTextTrivia)
            || (trivia.SpanStart < position && trivia.Span.Contains(position)
                && !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.EndOfLineTrivia));
    }

    private sealed record BodyLine(
        TextLine Text, string Indentation, bool IsInsideBlock, bool IsProtected, bool IsDirective)
    {
        public bool IsBlank => Indentation.Length == Text.Span.Length;
    }
}
