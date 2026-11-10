using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>A recoverable index generation. Disposal without Publish leaves the previous snapshot visible.</summary>
public interface IIndexUpdate : IDisposable
{
    /// <summary>Reuses a matching file from an earlier partial or published generation of the same branch; false means it needs analysis.</summary>
    bool TryReuse(IndexedSourceFile file, CancellationToken cancellationToken = default);

    void AddFile(IndexedSourceFile file, IReadOnlyList<SymbolDefinition> symbols, CancellationToken cancellationToken = default);

    /// <summary>Copies existing references only when every indexed file and the analyzer configuration are unchanged.</summary>
    bool TryReuseReferences(CancellationToken cancellationToken = default);

    /// <summary>Adds resolved references; reports persisted row counts without exposing source code.</summary>
    void AddReferences(IReadOnlyList<SymbolReference> references, CancellationToken cancellationToken = default,
        Action<int>? onProgress = null);

    /// <summary>Atomically publishes the branch's active snapshot, retaining other folders for cumulative updates.</summary>
    RepositoryIndexSnapshot Publish(CancellationToken cancellationToken = default);
}
