using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Language-specific parsing and declaration extraction contract.</summary>
public interface ISourceAnalyzer
{
    /// <summary>Stable adapter ID referenced by codesemantic4llm.yaml.</summary>
    string AnalyzerId { get; }

    /// <summary>Source language identity, independent of the analyzer implementation ID.</summary>
    string LanguageId { get; }

    /// <summary>Default discovery extensions, replaceable per repository through YAML configuration.</summary>
    SourceFileExtensions DefaultFileExtensions { get; }

    /// <summary>Whether a path matches this adapter's defaults, independently of repository overrides.</summary>
    bool Supports(string relativePath);

    Task<IReadOnlyList<SymbolDefinition>> AnalyzeAsync(
        SourceDocument document,
        CancellationToken cancellationToken = default);
}
