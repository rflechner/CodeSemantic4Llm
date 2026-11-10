using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using TreeSitter;

namespace CodeSemantic4Llm.Analysis.TypeScript;

/// <summary>Owns the native parser and tree for a single operation; no source text escapes into persistent storage.</summary>
internal static class TypeScriptSyntaxTree
{
    public static TResult Read<TResult>(
        SourceDocument document, Func<Node, TResult> read, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(read);
        cancellationToken.ThrowIfCancellationRequested();

        // The bundled TypeScript grammar does not include the distinct TSX grammar.
        if (document.RelativePath.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("TSX requires a separate Tree-sitter grammar and is not supported by this analyzer.");
        }

        using var language = new Language("TypeScript");
        using var parser = new Parser(language);
        using var tree = parser.Parse(document.Content)
            ?? throw new InvalidOperationException($"Tree-sitter could not parse '{document.RelativePath}'.");

        cancellationToken.ThrowIfCancellationRequested();
        return read(tree.RootNode);
    }

    public static SourceLocation Location(string path, Node node)
    {
        var position = node.StartPosition;
        return new SourceLocation(path, position.Row + 1, position.Column + 1);
    }
}
