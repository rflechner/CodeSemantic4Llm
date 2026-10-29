using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Reads an individual repository's root configuration. Never searches parent directories.</summary>
public interface IRepositoryConfigurationLoader
{
    Task<RepositoryIndexingConfiguration> LoadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}
