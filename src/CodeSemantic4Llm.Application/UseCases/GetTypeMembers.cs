using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Lists members by an explicit source declaration, avoiding guesses from qualified-name prefixes.</summary>
public sealed class GetTypeMembers(IIndexStore indexStore, IRepositoryStateProvider stateProvider)
{
    private static readonly string[] MemberKinds =
        ["method", "constructor", "property", "field", "event", "class", "struct", "interface", "record", "enum", "enum-member", "delegate"];

    public async Task<TypeMemberPage> ExecuteAsync(
        string repositoryRoot, SourceLocation typeLocation, long snapshotId,
        string? kind = null, int maxResults = 50, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(typeLocation);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(snapshotId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (maxResults > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "Limit must not exceed 200.");
        }

        if (kind is not null && !MemberKinds.Contains(kind, StringComparer.Ordinal))
        {
            throw new ArgumentException($"Unknown member kind '{kind}'. Supported kinds: {string.Join(", ", MemberKinds)}.", nameof(kind));
        }

        ValidateRelativePath(typeLocation.RelativePath);
        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var result = indexStore.GetTypeMembers(state.Scope, typeLocation, snapshotId, kind, maxResults, offset, cancellationToken);

        if (result.Snapshot.RecordedRevision != state.Revision)
        {
            throw new InvalidOperationException("HEAD has changed since this index was recorded. Run index again before searching.");
        }

        return result;
    }

    private static void ValidateRelativePath(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':') || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Use the repository-relative path returned by find_symbols.", nameof(path));
        }
    }
}
