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

    public async Task<MethodBodyWriteResult> ReplaceAsync(
        string repositoryRoot, SourceLocation declaration, long snapshotId,
        string expectedContentHash, string newBody, CancellationToken cancellationToken = default)
    {
        if (!writeAccess.Enabled)
        {
            throw new InvalidOperationException(
                "Repository writes are disabled. Restart the MCP server with 'serve --allow-write' to enable editing.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentHash);
        ArgumentNullException.ThrowIfNull(newBody);

        var selected = await SelectAsync(repositoryRoot, declaration, snapshotId, cancellationToken);

        if (!string.Equals(selected.Source.ContentHash, expectedContentHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The source file differs from the expected hash. Read the method again before editing.");
        }

        var source = methodBodyEditor.Replace(selected.Source.Document, declaration, newBody);

        if (string.Equals(source, selected.Source.Document.Content, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The replacement body is identical to the current body.");
        }

        var hash = await sourceWriter.ReplaceAsync(
            selected.Method.Snapshot.Scope.RootPath, declaration.RelativePath,
            expectedContentHash, source, cancellationToken);

        return new MethodBodyWriteResult(declaration, hash, ReindexRequired: true);
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

/// <summary>A successful checkout edit; the persisted index intentionally stays unchanged.</summary>
public sealed record MethodBodyWriteResult(SourceLocation MethodLocation, string ContentHash, bool ReindexRequired);
