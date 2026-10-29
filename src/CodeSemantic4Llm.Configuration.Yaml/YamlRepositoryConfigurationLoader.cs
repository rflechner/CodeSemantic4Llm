using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeSemantic4Llm.Configuration.Yaml;

/// <summary>
/// Loads only codesemantic4llm.yaml from the supplied repository root.
/// Unknown properties, duplicate YAML keys, and incompatible versions are rejected.
/// </summary>
public sealed class YamlRepositoryConfigurationLoader : IRepositoryConfigurationLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .Build();

    public async Task<RepositoryIndexingConfiguration> LoadAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var configurationPath = Path.Combine(
            Path.GetFullPath(repositoryRoot),
            RepositoryIndexingConfiguration.FileName);

        if (!File.Exists(configurationPath))
        {
            throw new RepositoryConfigurationException(
                $"Missing repository configuration: '{configurationPath}'. " +
                $"Create {RepositoryIndexingConfiguration.FileName} at the repository root.");
        }

        string yaml;

        try
        {
            yaml = await File.ReadAllTextAsync(configurationPath, cancellationToken);
        }
        catch (IOException exception)
        {
            throw new RepositoryConfigurationException(
                $"Cannot read repository configuration '{configurationPath}': {exception.Message}",
                exception);
        }

        try
        {
            var document = Deserializer.Deserialize<RepositoryYamlDocument>(yaml)
                ?? throw new RepositoryConfigurationException("The repository configuration is empty.");

            if (document.Version != RepositoryIndexingConfiguration.CurrentVersion)
            {
                throw new RepositoryConfigurationException(
                    $"Unsupported configuration version '{document.Version}' in '{configurationPath}'. " +
                    $"Expected version {RepositoryIndexingConfiguration.CurrentVersion}.");
            }

            if (document.Analyzers is null || document.Analyzers.Count == 0)
            {
                throw new RepositoryConfigurationException(
                    $"Configuration '{configurationPath}' must declare at least one analyzer.");
            }

            return new RepositoryIndexingConfiguration(document.Analyzers, document.Exclude);
        }
        catch (YamlException exception)
        {
            throw new RepositoryConfigurationException(
                $"Invalid YAML in '{configurationPath}': {exception.Message}", exception);
        }
        catch (ArgumentException exception)
        {
            throw new RepositoryConfigurationException(
                $"Invalid indexing configuration in '{configurationPath}': {exception.Message}", exception);
        }
    }

    private sealed class RepositoryYamlDocument
    {
        public int Version { get; set; }

        public List<string>? Analyzers { get; set; }

        public List<string>? Exclude { get; set; }
    }
}
