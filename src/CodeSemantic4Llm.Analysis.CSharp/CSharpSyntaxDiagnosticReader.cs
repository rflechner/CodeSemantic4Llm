using System.Globalization;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>Maps parser diagnostics to stable, physical source locations, optionally within a declaration.</summary>
internal static class CSharpSyntaxDiagnosticReader
{
    public static IReadOnlyList<SyntaxDiagnostic> Read(
        SyntaxTree tree, TextSpan? declarationSpan = null, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<SyntaxDiagnostic>();

        foreach (var diagnostic in tree.GetDiagnostics(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!diagnostic.Location.IsInSource ||
                diagnostic.Severity is not (DiagnosticSeverity.Error or DiagnosticSeverity.Warning))
            {
                continue;
            }

            var start = diagnostic.Location.SourceSpan.Start;

            // Missing tokens can be anchored at the closing boundary, including EOF.
            if (declarationSpan is { } span && (start < span.Start || start > span.End))
            {
                continue;
            }

            var position = diagnostic.Location.GetLineSpan().StartLinePosition;
            diagnostics.Add(new SyntaxDiagnostic(
                diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(),
                diagnostic.GetMessage(CultureInfo.InvariantCulture),
                new SourceLocation(tree.FilePath, position.Line + 1, position.Character + 1)));
        }

        return diagnostics.Distinct()
            .OrderBy(diagnostic => diagnostic.Location.Line)
            .ThenBy(diagnostic => diagnostic.Location.Column)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
    }
}
