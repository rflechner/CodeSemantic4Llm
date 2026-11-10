using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Analysis.TypeScript;

/// <summary>Tree-sitter TypeScript declaration analyzer. No type-checking or cross-file name resolution is inferred.</summary>
public sealed class TypeScriptSourceAnalyzer : ISourceAnalyzer
{
    public string AnalyzerId => "typescript";

    public string LanguageId => "typescript";

    public SourceFileExtensions DefaultFileExtensions { get; } = new([".ts", ".mts", ".cts"]);

    public bool Supports(string relativePath) => DefaultFileExtensions.Matches(relativePath);

    public Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
        SourceDocument document, CancellationToken cancellationToken = default)
    {
        var symbols = TypeScriptSyntaxTree.Read(document,
            root => TypeScriptDeclarations.Read(document, root, cancellationToken)
                .Select(declaration => declaration.Symbol).ToArray(),
            cancellationToken);

        return Task.FromResult<IReadOnlyList<SymbolDefinition>>(symbols);
    }
}
