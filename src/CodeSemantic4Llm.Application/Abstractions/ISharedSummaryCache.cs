using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Repository-local, versionable descriptions. Reads never modify the checkout.</summary>
public interface ISharedSummaryCache
{
    Task<SharedSummaryRead> ReadAsync(string root, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Writes one complete document atomically. Different existing text requires explicit overwrite.</summary>
    Task<bool> WriteAsync(string root, SharedSummaryFile file, bool overwrite, CancellationToken cancellationToken = default);
}
