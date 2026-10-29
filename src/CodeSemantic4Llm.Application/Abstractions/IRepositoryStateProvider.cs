using CodeSemantic4Llm.Domain.Repositories;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Reads a checkout's canonical root, branch and HEAD without executing project code.</summary>
public interface IRepositoryStateProvider
{
    Task<RepositoryState> GetStateAsync(string repositoryRoot, CancellationToken cancellationToken = default);
}
