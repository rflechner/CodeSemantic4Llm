namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Writes a checkout source file after checking its exact existing byte hash.</summary>
public interface IRepositorySourceWriter
{
    /// <summary>
    /// Atomically publishes a UTF-8 replacement where supported, preserving any UTF-8 BOM.
    /// Refuses traversal, symbolic links and a stale expected hash.
    /// </summary>
    Task<string> ReplaceAsync(
        string repositoryRoot, string relativePath, string expectedContentHash,
        string replacementSource, CancellationToken cancellationToken = default);
}
