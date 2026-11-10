using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Analysis.CSharp;

/// <summary>
/// Indexes Blazor components and classic .cshtml views. Embedded C# members are parsed with
/// Roslyn, while Razor markup and directives contribute searchable document metadata.
/// </summary>
public sealed class RazorSourceAnalyzer : ISourceAnalyzer
{
    public string AnalyzerId => "dotnet-razor";

    public string LanguageId => "razor";

    public SourceFileExtensions DefaultFileExtensions { get; } = new([".razor", ".cshtml"]);

    public bool Supports(string relativePath) => DefaultFileExtensions.Matches(relativePath);

    public Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
        SourceDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var declarations = RazorDocumentParser.Parse(document, cancellationToken);
        return Task.FromResult<IReadOnlyList<SymbolDefinition>>(
            declarations.Select(declaration => declaration.Symbol).ToArray());
    }
}
