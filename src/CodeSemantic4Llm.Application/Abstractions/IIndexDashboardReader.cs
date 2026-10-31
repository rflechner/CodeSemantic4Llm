namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>
/// Read-only, cache-wide exploration of published indexes. Project ids identify cache entries,
/// not paths provided by HTTP clients. No operation reads or changes source files.
/// </summary>
public interface IIndexDashboardReader
{
    /// <summary>Lists every valid indexed checkout and its published Git branches.</summary>
    IReadOnlyList<DashboardProject> ListProjects(CancellationToken cancellationToken = default);

    /// <summary>Returns language/kind distributions and reference counts for an active branch.</summary>
    DashboardSummary GetSummary(string projectId, string branch, CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded page of indexed files and their persistence metadata.</summary>
    DashboardPage<DashboardFile> GetFiles(
        string projectId, string branch, string? search, int offset, int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a bounded page of declarations, optionally narrowed by name, kind or file.</summary>
    DashboardPage<DashboardSymbol> GetSymbols(
        string projectId, string branch, string? search, string? kind, string? file,
        int offset, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns recorded semantic usages. Target coordinates, when supplied, identify one declaration;
    /// missing reference metadata on a legacy snapshot is represented as an empty page.
    /// </summary>
    DashboardPage<DashboardReference> GetReferences(
        string projectId, string branch, string? search, string? targetPath, int? targetLine, int? targetColumn,
        int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>One checkout in the index cache, with only its currently published branches.</summary>
public sealed record DashboardProject(
    string Id, string Name, string RootPath, IReadOnlyList<DashboardBranch> Branches);

/// <summary>Published snapshot metadata; the recorded revision is not a current-working-tree guarantee.</summary>
public sealed record DashboardBranch(
    string Name, long SnapshotId, string? RecordedRevision, string SemanticLanguage,
    DateTimeOffset CompletedAt, int FileCount, int SymbolCount,
    bool HasTypeMembership, bool HasReferences);

/// <summary>Aggregated declaration and reference information for a published snapshot.</summary>
public sealed record DashboardSummary(
    long ReferenceCount, IReadOnlyList<DashboardFacet> Kinds, IReadOnlyList<DashboardFacet> Languages);

/// <summary>A named aggregate within a snapshot.</summary>
public sealed record DashboardFacet(string Name, long Count);

/// <summary>Persisted metadata for an indexed source file; source contents are never returned.</summary>
public sealed record DashboardFile(
    string Path, string AnalyzerId, string AnalyzerVersion, string ContentHash, int SymbolCount);

/// <summary>A declaration, including its exact source address and optional immediate containing type.</summary>
public sealed record DashboardSymbol(
    string Language, string Kind, string Name, string QualifiedName,
    string Path, int Line, int Column, int? ContainingTypeLine, int? ContainingTypeColumn,
    string? Summary = null);

/// <summary>A saved relation between a target declaration and an occurrence elsewhere in the checkout.</summary>
public sealed record DashboardReference(
    string TargetPath, int TargetLine, int TargetColumn, string? TargetQualifiedName,
    string UsagePath, int UsageLine, int UsageColumn);

/// <summary>A deterministic, bounded result page with the untruncated result count.</summary>
public sealed record DashboardPage<T>(IReadOnlyList<T> Items, long Total, int Offset, int Limit);
