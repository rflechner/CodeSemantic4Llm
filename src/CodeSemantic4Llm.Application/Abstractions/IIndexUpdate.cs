using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>A recoverable index generation. Disposal without Publish leaves the previous snapshot visible.</summary>
public interface IIndexUpdate : IDisposable
{
    /// <summary>Reuses a matching file from the previous snapshot; false means it needs analysis.</summary>
    bool TryReuse(IndexedSourceFile file, CancellationToken cancellationToken = default);

    void AddFile(IndexedSourceFile file, IReadOnlyList<SymbolDefinition> symbols, CancellationToken cancellationToken = default);

    /// <summary>Atomically replaces the branch's active snapshot and removes its superseded derived data.</summary>
    RepositoryIndexSnapshot Publish(CancellationToken cancellationToken = default);
}
