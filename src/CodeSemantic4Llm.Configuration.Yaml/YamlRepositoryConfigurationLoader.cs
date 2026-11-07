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
                        document.SemanticIndexing.Llm.ForceGeneration,
                        document.SemanticIndexing.Llm.SystemPrompt,
                        document.SemanticIndexing.Llm.Endpoints?.Select(endpoint =>
                            new LlmEndpointConfiguration(
                                endpoint?.BaseUrl ?? throw new ArgumentException("Each LLM endpoint needs a baseUrl."),
                                endpoint.ApiKeyEnvironmentVariable, endpoint.ApiKey)),
                        document.SemanticIndexing.Llm.MaxConcurrentRequestsPerEndpoint,
                        document.SemanticIndexing.Llm.RetryCount,
                        document.SemanticIndexing.Llm.RetryDelayMilliseconds,
                        document.SemanticIndexing.Llm.MaxConsecutiveFailedFiles));

            var fileExtensions = MapFileExtensions(document.FileExtensions);
            var tools = MapTools(document.Tools);

            return new RepositoryIndexingConfiguration(
                document.Analyzers, document.Exclude, semanticIndexing, fileExtensions, document.ProjectName, tools,
                document.SharedSummaries,
                document.Watch is null ? null : new RepositoryWatchConfiguration(document.Watch.DebounceSeconds));
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

    /// <summary>
    /// Returns null when the repository declares no tools section, which selects every tool.
    /// An explicitly written section must name at least one tool: commenting out every entry is
    /// a configuration error rather than a silent return to advertising all of them.
    /// </summary>
    private static McpToolSelection? MapTools(List<string>? tools)
    {
        if (ReferenceEquals(tools, RepositoryYamlDocument.MissingTools))
        {
            return null;
        }

        if (tools is null || tools.Count == 0)
        {
            throw new RepositoryConfigurationException(
                "The tools section must list at least one MCP tool. " +
                "Remove the section to advertise every tool.");
        }

        return new McpToolSelection(tools);
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
        /// <summary>
        /// Identifies a document whose tools section is absent. The deserializer replaces this
        /// instance for any written section, including an empty one, so reference identity
        /// distinguishes "no section" from "a section that selects nothing".
        /// </summary>
        internal static readonly List<string> MissingTools = [];

        public int Version { get; set; }

        public bool SharedSummaries { get; set; }

        public string? ProjectName { get; set; }

        public List<string>? Analyzers { get; set; }

        public List<string>? Exclude { get; set; }

        public List<string>? Tools { get; set; } = MissingTools;

        public Dictionary<string, List<string>?>? FileExtensions { get; set; } = new();

        public SemanticIndexingYamlDocument? SemanticIndexing { get; set; }

        public WatchYamlDocument? Watch { get; set; }
    }

    private sealed class WatchYamlDocument
    {
        public int DebounceSeconds { get; set; } = RepositoryWatchConfiguration.DefaultDebounceSeconds;
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

        public string? SystemPrompt { get; set; }

        public string? BaseUrl { get; set; }

        public string? Model { get; set; }

        public string? ApiKeyEnvironmentVariable { get; set; }

        public string? ApiKey { get; set; }

        public string? ReasoningEffort { get; set; }

        public int? MaxCompletionTokens { get; set; }

        public List<LlmEndpointYamlDocument?>? Endpoints { get; set; }

        public int MaxConcurrentRequestsPerEndpoint { get; set; } =
            LlmSummaryConfiguration.DefaultMaxConcurrentRequestsPerEndpoint;

        public int RetryCount { get; set; } = LlmSummaryConfiguration.DefaultRetryCount;

        public int RetryDelayMilliseconds { get; set; } = LlmSummaryConfiguration.DefaultRetryDelayMilliseconds;

        public int MaxConsecutiveFailedFiles { get; set; } =
            LlmSummaryConfiguration.DefaultMaxConsecutiveFailedFiles;
    }

    private sealed class LlmEndpointYamlDocument
    {
        public string? BaseUrl { get; set; }
        public string? ApiKeyEnvironmentVariable { get; set; }
        public string? ApiKey { get; set; }
    }
}
