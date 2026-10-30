using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Writing is a server-startup capability; an MCP request cannot enable it.</summary>
public sealed record RepositoryWriteAccess(bool Enabled);

/// <summary>Bounded source reading and opt-in method body replacement by exact indexed declaration.</summary>
public sealed class MethodBodyOperations(
    IRepositoryStateProvider stateProvider,
    IIndexStore indexStore,
    IRepositorySourceReader sourceReader,
    IRepositorySourceWriter sourceWriter,
    IMethodBodyEditor methodBodyEditor,
    IMethodDeclarationEditor declarationEditor,
    IMethodSyntaxDiagnostics syntaxDiagnostics,
    RepositoryWriteAccess writeAccess)
{
    public async Task<MethodBodyReadResult> ReadAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        int maxCharacters = 12000, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (maxCharacters > 20000)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCharacters), "Limit must not exceed 20000.");
        }

        var selected = await SelectAsync(repositoryRoot, declaration, snapshotId, cancellationToken);
        var body = methodBodyEditor.Read(selected.Source.Document, declaration);

        var start = Math.Min(offset, body.Text.Length);
        var length = Math.Min(maxCharacters, body.Text.Length - start);
        int? nextOffset = start + length < body.Text.Length ? start + length : null;

        return new MethodBodyReadResult(
            selected.Method.Snapshot, selected.Method.Method, selected.Source.ContentHash,
            new MethodBody(body.Text.Substring(start, length), body.IsExpressionBodied),
            body.Text.Length, nextOffset);
    }

    /// <summary>Reads one indexed method's attributes, signature and implementation.</summary>
    public async Task<MethodDeclarationReadResult> ReadDeclarationAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        int maxCharacters = 12000, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxCharacters, 20000);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        var selected = await SelectAsync(repositoryRoot, declaration, snapshotId, cancellationToken);
        var text = declarationEditor.Read(selected.Source.Document, declaration);
        var start = Math.Min(offset, text.Length);
        var length = Math.Min(maxCharacters, text.Length - start);
        return new MethodDeclarationReadResult(selected.Method.Snapshot, selected.Method.Method,
            selected.Source.ContentHash, text.Substring(start, length), text.Length,
            start + length < text.Length ? start + length : null);
    }

    public Task<MethodBodyWriteResult> ReplaceAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        string expectedContentHash, string newBody, CancellationToken cancellationToken = default) =>
        ReplaceSelectedAsync(repositoryRoot, declaration, snapshotId, expectedContentHash,
            document => new MethodDeclarationEdit(methodBodyEditor.Replace(document, declaration, newBody), declaration),
            cancellationToken);

    /// <summary>Replaces an entire indexed method while preserving hash and snapshot checks.</summary>
    public Task<MethodBodyWriteResult> ReplaceDeclarationAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        string expectedContentHash, string newMethod, CancellationToken cancellationToken = default) =>
        ReplaceSelectedAsync(repositoryRoot, declaration, snapshotId, expectedContentHash,
            document => declarationEditor.Replace(document, declaration, newMethod), cancellationToken);

    private async Task<MethodBodyWriteResult> ReplaceSelectedAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId, string expectedContentHash,
        Func<SourceDocument, MethodDeclarationEdit> replace, CancellationToken cancellationToken)
    {
        if (!writeAccess.Enabled)
        {
            throw new InvalidOperationException(
                "Repository writes are disabled. Restart the MCP server with 'serve --allow-write' to enable editing.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentHash);

        var selected = await SelectAsync(repositoryRoot, declaration, snapshotId, cancellationToken);

        if (!string.Equals(selected.Source.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file differs from the expected hash. Read the method again before editing.");
        }

        var edit = replace(selected.Source.Document);

        if (string.Equals(edit.Content, selected.Source.Document.Content, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The replacement is identical to the current method.");
        }

        var hash = await sourceWriter.ReplaceAsync(
            selected.Method.Snapshot.Scope.RootPath, declaration.RelativePath,
            expectedContentHash, edit.Content, cancellationToken);

        return new MethodBodyWriteResult(edit.MethodLocation, hash, ReindexRequired: true);
    }

    /// <summary>
    /// Checks a proposed C# block against an unchanged indexed method, entirely in memory.
    /// This stays available in read-only mode and never attempts a source-file write.
    /// </summary>
    public async Task<MethodDiagnosticsResult> ValidateAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        string expectedContentHash, string newBody,
        int maxDiagnostics = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentHash);
        ValidatePage(maxDiagnostics, offset);
        ArgumentNullException.ThrowIfNull(newBody);

        var selected = await SelectAsync(repositoryRoot, declaration, snapshotId, cancellationToken);

        if (!string.Equals(selected.Source.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file differs from the expected hash. Read the method again before validating.");
        }

        var diagnostics = syntaxDiagnostics.ValidateReplacement(
            selected.Source.Document, declaration, newBody);

        return ToPage(declaration, selected.Source.ContentHash, diagnostics, maxDiagnostics, offset);
    }

    /// <summary>
    /// Checks the current checkout text without consulting a possibly stale SQLite snapshot.
    /// This remains usable immediately after a method-body edit, before the next index run.
    /// </summary>
    public async Task<MethodDiagnosticsResult> GetDiagnosticsAsync(
        string repositoryRoot, SourceLocation declaration, string expectedMethodName,
        int maxDiagnostics = 50, int offset = 0, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedMethodName);
        ValidatePage(maxDiagnostics, offset);
        ValidateRelativePath(declaration.RelativePath);
        cancellationToken.ThrowIfCancellationRequested();

        var source = await sourceReader.ReadAsync(repositoryRoot, declaration.RelativePath, cancellationToken);
        var diagnostics = syntaxDiagnostics.GetCurrent(
            source.Document, declaration, expectedMethodName);

        return ToPage(declaration, source.ContentHash, diagnostics, maxDiagnostics, offset);
    }

    private static MethodDiagnosticsResult ToPage(
        SourceLocation declaration, string contentHash,
        IReadOnlyList<SyntaxDiagnostic> diagnostics, int limit, int offset)
    {
        var results = diagnostics.Skip(offset).Take(limit).ToArray();
        int? nextOffset = diagnostics.Count - offset > limit ? checked(offset + limit) : null;

        return new MethodDiagnosticsResult(
            declaration, contentHash,
            diagnostics.All(diagnostic => diagnostic.Severity != "error"),
            results, diagnostics.Count, nextOffset);
    }

    private static void ValidatePage(int maxDiagnostics, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDiagnostics);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (maxDiagnostics > 200)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxDiagnostics), "Limit must not exceed 200.");
        }
    }

    private async Task<SelectedMethod> SelectAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotId);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRelativePath(declaration.RelativePath);

        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var method = indexStore.GetIndexedMethod(state.Scope, declaration, snapshotId, cancellationToken);

        if (method.Snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("HEAD has changed since this index was recorded. Run index again.");
        }

        var source = await sourceReader.ReadAsync(state.Scope.RootPath, declaration.RelativePath, cancellationToken);

        if (!string.Equals(source.ContentHash, method.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file has changed since indexing. Run index again before reading or editing this method.");
        }

        return new SelectedMethod(method, source);
    }

    private static void ValidateRelativePath(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':') ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Use the repository-relative path returned by find_symbols.", nameof(path));
        }
    }

    private sealed record SelectedMethod(IndexedMethod Method, SourceFileSnapshot Source);
}

/// <summary>One method's current body and the optimistic-concurrency token for an edit.</summary>
public sealed record MethodBodyReadResult(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Method, string ContentHash, MethodBody Body,
    int TotalCharacters, int? NextOffset);

/// <summary>One complete indexed declaration and the source version needed for editing.</summary>
public sealed record MethodDeclarationReadResult(
    RepositoryIndexSnapshot Snapshot, SymbolDefinition Method, string ContentHash, string Text,
    int TotalCharacters, int? NextOffset);

/// <summary>A successful checkout edit; the persisted index intentionally stays unchanged.</summary>
public sealed record MethodBodyWriteResult(SourceLocation MethodLocation, string ContentHash, bool ReindexRequired);


/// <summary>
/// A bounded page of syntax-only diagnostics, with validity computed across all pages.
/// ContentHash identifies the original source when validating or the current file when reading.
/// </summary>
public sealed record MethodDiagnosticsResult(
    SourceLocation MethodLocation, string ContentHash, bool IsValid,
    IReadOnlyList<SyntaxDiagnostic> Diagnostics, int TotalDiagnostics, int? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}
