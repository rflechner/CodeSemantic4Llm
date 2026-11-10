using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Steps of cross-file reference analysis; parsing and semantic binding are separate workloads.</summary>
public enum ReferenceAnalysisPhase { Parsing, PreparingCompilation, Resolving, Sorting }

/// <summary>Progress within one analyzer pass, measured in source files.</summary>
public sealed record ReferenceAnalysisProgress(ReferenceAnalysisPhase Phase, int CompletedFiles, int TotalFiles);

/// <summary>
/// Resolves references across the selected files of one language in a transient compilation.
/// Results must point to declarations in the supplied documents and never contain source text.
/// </summary>
public interface ISourceReferenceAnalyzer
{
    string AnalyzerId { get; }

    /// <summary>Invalidates persisted references when the implementation changes.</summary>
    string IndexVersion => GetType().Assembly.ManifestModule.ModuleVersionId.ToString("N");

    Task<IReadOnlyList<SymbolReference>> AnalyzeAsync(
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken = default,
        IProgress<ReferenceAnalysisProgress>? progress = null);
}
