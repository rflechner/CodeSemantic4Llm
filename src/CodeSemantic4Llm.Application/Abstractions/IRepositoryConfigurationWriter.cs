using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Application.Abstractions;

/// <summary>Creates a complete configuration in an existing repository root without replacing an existing entry.</summary>
public interface IRepositoryConfigurationWriter
{
    /// <summary>Returns the absolute path of the created file. Failure must not publish a partial configuration.</summary>
    Task<string> CreateAsync(
        string repositoryRoot,
        RepositoryIndexingConfiguration configuration,
        CancellationToken cancellationToken = default);
}
