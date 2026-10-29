using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using YamlDotNet.Serialization;

namespace CodeSemantic4Llm.Configuration.Yaml;

/// <summary>Writes repository YAML without overwriting files, directories, or symbolic links at the destination.</summary>
public sealed class YamlRepositoryConfigurationWriter : IRepositoryConfigurationWriter
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithIndentedSequences()
        .Build();

    public async Task<string> CreateAsync(
        string repositoryRoot,
        RepositoryIndexingConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(repositoryRoot);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Repository directory '{root}' does not exist or is not a directory.");
        }

        var configurationPath = Path.Combine(root, RepositoryIndexingConfiguration.FileName);

        if (Path.Exists(configurationPath))
        {
            throw new RepositoryConfigurationException(
                $"Repository configuration already exists: '{configurationPath}'. No changes were made.");
        }

        var yaml = Serialize(configuration);
        var temporaryPath = Path.Combine(root, $".{RepositoryIndexingConfiguration.FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await WriteTemporaryFileAsync(temporaryPath, yaml, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            // Publishing in the same directory prevents partial YAML from becoming visible.
            // Refusing replacement also protects files created after the existence check above.
            File.Move(temporaryPath, configurationPath, overwrite: false);
            return configurationPath;
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string Serialize(RepositoryIndexingConfiguration configuration)
    {
        var document = new Dictionary<string, object>
        {
            ["version"] = configuration.Version
        };

        if (configuration.ProjectName is not null)
        {
            document["projectName"] = configuration.ProjectName;
        }

        document["analyzers"] = configuration.AnalyzerIds.ToArray();

        if (configuration.FileExtensions.Count > 0)
        {
            document["fileExtensions"] = configuration.FileExtensions.ToDictionary(
                entry => entry.Key, entry => entry.Value.ToArray());
        }

        document["semanticIndexing"] = new Dictionary<string, string>
        {
            ["language"] = configuration.SemanticIndexing.Language
        };

        // Built-in exclusions apply when loading; only repository-specific rules belong in YAML.
        document["exclude"] = configuration.Exclusions
            .Except(RepositoryIndexingConfiguration.BuiltInExclusions, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Serializer.Serialize(document);
    }

    private static async Task WriteTemporaryFileAsync(
        string path,
        string yaml,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(stream);

        await writer.WriteAsync(yaml.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }
}
