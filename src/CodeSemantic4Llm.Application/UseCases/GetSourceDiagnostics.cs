using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Checks current checkout files without reading or creating an index snapshot.</summary>
public sealed class GetSourceDiagnostics(
    IRepositorySourceReader sourceReader,
    IRepositoryConfigurationLoader configurationLoader,
    IEnumerable<ISourceAnalyzer> sourceAnalyzers,
    IEnumerable<ISourceSyntaxDiagnostics> syntaxAnalyzers)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _sourceAnalyzers = sourceAnalyzers.ToArray();
    private readonly IReadOnlyList<ISourceSyntaxDiagnostics> _syntaxAnalyzers = syntaxAnalyzers.ToArray();

    /// <summary>Returns a bounded page of all file diagnostics, even when declarations cannot be recovered.</summary>
    public Task<SourceDiagnosticsResult> GetFileAsync(
        string repositoryRoot, string relativePath, int maxDiagnostics = 50, int offset = 0,
        string? expectedContentHash = null, CancellationToken cancellationToken = default) =>
        ReadAsync(repositoryRoot, relativePath, null, null, maxDiagnostics, offset,
            expectedContentHash, cancellationToken);

    /// <summary>Checks one type part at its current identifier position, including its nested declarations.</summary>
    public Task<SourceDiagnosticsResult> GetTypeAsync(
        string repositoryRoot, SourceLocation declaration, string expectedTypeName,
        int maxDiagnostics = 50, int offset = 0, string? expectedContentHash = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedTypeName);

        return ReadAsync(repositoryRoot, declaration.RelativePath, declaration, expectedTypeName,
            maxDiagnostics, offset, expectedContentHash, cancellationToken);
    }

    private async Task<SourceDiagnosticsResult> ReadAsync(
        string repositoryRoot, string relativePath, SourceLocation? declaration, string? expectedTypeName,
        int maxDiagnostics, int offset, string? expectedContentHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ValidatePage(maxDiagnostics, offset, expectedContentHash);
        cancellationToken.ThrowIfCancellationRequested();

        var analyzer = await SelectAnalyzerAsync(repositoryRoot, relativePath, cancellationToken);
        var source = await sourceReader.ReadAsync(repositoryRoot, relativePath, cancellationToken);

        if (expectedContentHash is not null &&
            !string.Equals(source.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file has changed since the previous diagnostics page. Restart at offset 0.");
        }

        var diagnostics = declaration is null
            ? analyzer.GetFile(source.Document, cancellationToken)
            : analyzer.GetType(source.Document, declaration, expectedTypeName!, cancellationToken);

        int? nextOffset = diagnostics.Count - offset > maxDiagnostics ? checked(offset + maxDiagnostics) : null;

        return new SourceDiagnosticsResult(
            source.Document.RelativePath, declaration, source.ContentHash,
            diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            diagnostics.Skip(offset).Take(maxDiagnostics).ToArray(), diagnostics.Count, nextOffset);
    }

    private async Task<ISourceSyntaxDiagnostics> SelectAnalyzerAsync(
        string repositoryRoot, string relativePath, CancellationToken cancellationToken)
    {
        var configuration = await configurationLoader.LoadAsync(repositoryRoot, cancellationToken);
        var selected = new ConfiguredSourceAnalyzers(configuration, _sourceAnalyzers).Select(relativePath)
            ?? throw new NotSupportedException($"No configured source analyzer supports '{relativePath}'.");

        return _syntaxAnalyzers.SingleOrDefault(analyzer => analyzer.AnalyzerId == selected.AnalyzerId)
            ?? throw new NotSupportedException(
                $"Syntax diagnostics are not implemented for analyzer '{selected.AnalyzerId}'.");
    }

    private static void ValidatePage(int maxDiagnostics, int offset, string? expectedContentHash)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDiagnostics);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (maxDiagnostics > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDiagnostics), "Limit must not exceed 200.");
        }

        if (offset > 0 || expectedContentHash is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentHash);
        }
    }
}

/// <summary>
/// A page of current-file diagnostics. TypeLocation is null for a whole-file query.
/// IsValid considers every diagnostic, not just this page. ContentHash identifies the bytes read,
/// not a Git revision or an index snapshot, and must be supplied when continuing pagination.
/// </summary>
public sealed record SourceDiagnosticsResult(
    string RelativePath, SourceLocation? TypeLocation, string ContentHash, bool IsValid,
    IReadOnlyList<SyntaxDiagnostic> Diagnostics, int TotalDiagnostics, int? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}
