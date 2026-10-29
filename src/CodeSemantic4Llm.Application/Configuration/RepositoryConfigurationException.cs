namespace CodeSemantic4Llm.Application.Configuration;

/// <summary>A human-readable error in a repository-local indexing configuration.</summary>
public sealed class RepositoryConfigurationException : Exception
{
    public RepositoryConfigurationException(string message) : base(message)
    {
    }

    public RepositoryConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
