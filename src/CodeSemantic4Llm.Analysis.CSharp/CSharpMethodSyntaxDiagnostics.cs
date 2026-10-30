using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
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
        var (replacement, hasValidShape, syntaxErrors) = CSharpMethodBodyEditor.InspectReplacement(document, method, newBody);

        if (!hasValidShape)
        {
            return
            [
                new SyntaxDiagnostic(
                    "CS4L0001", "error",
                    CSharpMethodBodyEditor.BodyInputHelp,
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
            return CSharpSyntaxDiagnosticReader.ReadReplacementErrors(
                syntaxErrors, document.RelativePath, SourceText.From(proposed), span.Start);
        }

        var updated = new SourceDocument(document.RelativePath, proposed);
        return GetCurrent(updated, declaration, method.Identifier.ValueText);
    }

}
