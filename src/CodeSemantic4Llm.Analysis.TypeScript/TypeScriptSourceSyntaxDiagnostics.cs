using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using TreeSitter;

namespace CodeSemantic4Llm.Analysis.TypeScript;

/// <summary>Reports Tree-sitter parse errors for watch validation and read-only MCP diagnostics.</summary>
public sealed class TypeScriptSourceSyntaxDiagnostics : ISourceSyntaxDiagnostics
{
    public string AnalyzerId => "typescript";

    public IReadOnlyList<SyntaxDiagnostic> GetFile(
        SourceDocument document, CancellationToken cancellationToken = default) =>
        TypeScriptSyntaxTree.Read(document,
            root => ReadDiagnostics(document, root, cancellationToken), cancellationToken);

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

        return TypeScriptSyntaxTree.Read(document, root =>
        {
            var type = TypeScriptDeclarations.Read(document, root, cancellationToken)
                .SingleOrDefault(item =>
                    item.Symbol.Kind is "class" or "interface" or "enum" or "type-alias"
                    && item.Symbol.Name == expectedTypeName
                    && item.Symbol.Location == declaration);

            if (type is null)
            {
                throw new InvalidOperationException(
                    "No TypeScript type exists at the selected declaration location with the expected name. " +
                    "Use get_file_diagnostics if the declaration moved or became invalid.");
            }

            return ReadDiagnostics(document, type.Syntax, cancellationToken);
        }, cancellationToken);
    }

    private static IReadOnlyList<SyntaxDiagnostic> ReadDiagnostics(
        SourceDocument document, Node root, CancellationToken cancellationToken)
    {
        var diagnostics = new List<SyntaxDiagnostic>();
        Visit(root);
        return diagnostics;

        void Visit(Node node)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node.IsMissing || node.IsError)
            {
                diagnostics.Add(new SyntaxDiagnostic(
                    node.IsMissing ? "TS_MISSING_TOKEN" : "TS_PARSE_ERROR",
                    "error",
                    node.IsMissing ? $"Missing TypeScript syntax: {node.Type}." : "Unrecognized TypeScript syntax.",
                    TypeScriptSyntaxTree.Location(document.RelativePath, node)));
                return;
            }

            foreach (var child in node.NamedChildren)
            {
                Visit(child);
            }
        }
    }
}
