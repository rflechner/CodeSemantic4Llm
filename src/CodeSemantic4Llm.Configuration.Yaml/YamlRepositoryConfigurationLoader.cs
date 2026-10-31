using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;
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

            var semanticIndexing = document.SemanticIndexing is null
                ? new SemanticIndexingConfiguration()
                : new SemanticIndexingConfiguration(document.SemanticIndexing.Language,
                    document.SemanticIndexing.Llm is null ? null : new LlmSummaryConfiguration(
                        document.SemanticIndexing.Llm.Enabled,
                        document.SemanticIndexing.Llm.BaseUrl,
                        document.SemanticIndexing.Llm.Model,
                        document.SemanticIndexing.Llm.ApiKeyEnvironmentVariable,
                        document.SemanticIndexing.Llm.ApiKey,
                        document.SemanticIndexing.Llm.ReasoningEffort,
                        document.SemanticIndexing.Llm.MaxCompletionTokens,
                        document.SemanticIndexing.Llm.ForceGeneration));

            var fileExtensions = MapFileExtensions(document.FileExtensions);

            return new RepositoryIndexingConfiguration(
                document.Analyzers, document.Exclude, semanticIndexing, fileExtensions, document.ProjectName);
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

    private static IReadOnlyDictionary<string, SourceFileExtensions> MapFileExtensions(
        Dictionary<string, List<string>?>? overrides)
    {
        if (overrides is null)
        {
            throw new RepositoryConfigurationException("fileExtensions must be a mapping of analyzer IDs to extension lists.");
        }

        return overrides.ToDictionary(
            entry => entry.Key,
            entry => new SourceFileExtensions(entry.Value
                ?? throw new RepositoryConfigurationException(
                    $"File extensions for analyzer '{entry.Key}' must be a non-empty list.")));
    }

    private sealed class RepositoryYamlDocument
    {
        public int Version { get; set; }

        public string? ProjectName { get; set; }

        public List<string>? Analyzers { get; set; }

        public List<string>? Exclude { get; set; }

        public Dictionary<string, List<string>?>? FileExtensions { get; set; } = new();

        public SemanticIndexingYamlDocument? SemanticIndexing { get; set; }
    }

    private sealed class SemanticIndexingYamlDocument
    {
        public string? Language { get; set; } = SemanticIndexingConfiguration.DefaultLanguage;

        public LlmSummaryYamlDocument? Llm { get; set; }
    }

    private sealed class LlmSummaryYamlDocument
    {
        public bool Enabled { get; set; }

        public bool ForceGeneration { get; set; }

        public string? BaseUrl { get; set; }

        public string? Model { get; set; }

        public string? ApiKeyEnvironmentVariable { get; set; }

        public string? ApiKey { get; set; }

        public string? ReasoningEffort { get; set; }

        public int? MaxCompletionTokens { get; set; }
    }
}
