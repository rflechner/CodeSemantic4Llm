using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Runs Roslyn syntax diagnostics on an exact method or a proposed body in memory.
/// Diagnostics are local to that declaration; no compilation or project execution occurs.
/// </summary>
public sealed class CSharpMethodSyntaxDiagnostics : IMethodSyntaxDiagnostics
{
    public IReadOnlyList<SyntaxDiagnostic> GetCurrent(
        SourceDocument document, SourceLocation declaration, string expectedMethodName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMethodName);
        var method = CSharpMethodBodyEditor.FindMethod(document, declaration, expectedMethodName);
        _ = CSharpMethodBodyEditor.GetBodySpan(method);

        return CSharpSyntaxDiagnosticReader.Read(method.SyntaxTree, method.Span);
    }

    public IReadOnlyList<SyntaxDiagnostic> ValidateReplacement(
        SourceDocument document, SourceLocation declaration, string newBody)
    {
        ArgumentNullException.ThrowIfNull(newBody);
        var method = CSharpMethodBodyEditor.FindMethod(document, declaration);
        var span = CSharpMethodBodyEditor.GetBodySpan(method);
        var (replacement, hasValidShape, syntaxErrors) = CSharpMethodBodyEditor.InspectReplacement(document, newBody);

        if (!hasValidShape)
        {
            return
            [
                new SyntaxDiagnostic(
                    "CS4L0001", "error",
                    "Supply C# statements or one complete block, with balanced braces and no surrounding declarations.",
                    declaration)
            ];
        }

        // Only in-memory text is changed. The source document and checkout remain untouched.
        var proposed = string.Concat(
            document.Content.AsSpan(0, span.Start), replacement, document.Content.AsSpan(span.End));

        if (syntaxErrors.Count > 0)
        {
            // Diagnostics from ParseStatement are relative to the proposed block.
            // Convert them to the file positions the agent would see after replacing it.
            var proposedText = SourceText.From(proposed);
            return syntaxErrors
                .Select(diagnostic => ToProposedDiagnostic(
                    diagnostic, document.RelativePath, proposedText, span.Start))
                .Distinct()
                .OrderBy(diagnostic => diagnostic.Location.Line)
                .ThenBy(diagnostic => diagnostic.Location.Column)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ToArray();
        }

        var updated = new SourceDocument(document.RelativePath, proposed);
        return GetCurrent(updated, declaration, method.Identifier.ValueText);
    }

    private static SyntaxDiagnostic ToProposedDiagnostic(
        Diagnostic diagnostic, string relativePath, SourceText source, int bodyStart)
    {
        var blockPosition = diagnostic.Location.IsInSource ? diagnostic.Location.SourceSpan.Start : 0;
        var position = Math.Clamp(bodyStart + blockPosition, 0, source.Length);
        var line = source.Lines.GetLinePosition(position);

        return new SyntaxDiagnostic(
            diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(),
            diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            new SourceLocation(relativePath, line.Line + 1, line.Character + 1));
    }
}
