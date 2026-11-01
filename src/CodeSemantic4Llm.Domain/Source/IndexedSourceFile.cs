namespace CodeSemantic4Llm.Domain.Source;

/// <summary>Derived metadata used to reuse a file's declarations without persisting its source.</summary>
public sealed record IndexedSourceFile(
    string RelativePath, string ContentHash, string AnalyzerId, string AnalyzerVersion, string SummaryCacheHash = "");
