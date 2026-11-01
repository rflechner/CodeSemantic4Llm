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

        document["tools"] = configuration.Tools.EnabledToolNames.ToArray();
        document["sharedSummaries"] = configuration.SharedSummaries;

        var llm = configuration.SemanticIndexing.Llm;
        var llmDocument = new Dictionary<string, object>
        {
            ["enabled"] = llm.Enabled
        };

        if (llm.ForceGeneration)
        {
            llmDocument["forceGeneration"] = true;
        }

        if (llm.SystemPrompt is not null)
        {
            llmDocument["systemPrompt"] = llm.SystemPrompt;
        }

        if (llm.BaseUrl is not null)
        {
            llmDocument["baseUrl"] = llm.BaseUrl.ToString();
        }

        if (llm.Model is not null)
        {
            llmDocument["model"] = llm.Model;
        }

        if (llm.ApiKeyEnvironmentVariable is not null)
        {
            llmDocument["apiKeyEnvironmentVariable"] = llm.ApiKeyEnvironmentVariable;
        }

        if (llm.ApiKey is not null)
        {
            llmDocument["apiKey"] = llm.ApiKey;
        }

        if (llm.ReasoningEffort is not null)
        {
            llmDocument["reasoningEffort"] = llm.ReasoningEffort;
        }

        if (llm.MaxCompletionTokens is { } maxCompletionTokens)
        {
            llmDocument["maxCompletionTokens"] = maxCompletionTokens;
        }

        document["semanticIndexing"] = new Dictionary<string, object>
        {
            ["language"] = configuration.SemanticIndexing.Language,
            ["llm"] = llmDocument
        };

        // Built-in exclusions apply when loading; only repository-specific rules belong in YAML.
        document["exclude"] = configuration.Exclusions
            .Except(RepositoryIndexingConfiguration.BuiltInExclusions, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var yaml = AnnotateToolSelection(Serializer.Serialize(document));
        return llm.SystemPrompt is null ? AnnotateSystemPrompt(yaml) : yaml;
    }

    /// <summary>Includes the built-in template as an editable, inactive YAML block scalar.</summary>
    private static string AnnotateSystemPrompt(string yaml)
    {
        var newLine = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = yaml.Split(newLine).ToList();
        var llmIndex = lines.IndexOf("  llm:");

        lines.InsertRange(llmIndex + 1,
        [
            "    # Optional replacement for the default system prompt. {lang} uses semanticIndexing.language.",
            "    # Uncomment the whole block to customize it; other braces are kept literally.",
            "    # systemPrompt: |-",
            .. SummaryPrompt.SystemTemplate.Split('\n').Select(line => "    #   " + line.TrimEnd('\r'))
        ]);

        return string.Join(newLine, lines);
    }

    /// <summary>
    /// Explains the generated tool list inside the file itself. The serializer emits no comments,
    /// so the guidance is inserted above the top-level sequence this writer just produced.
    /// </summary>
    private static string AnnotateToolSelection(string yaml)
    {
        var newLine = yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = yaml.Split(newLine).ToList();
        var toolsIndex = lines.IndexOf("tools:");

        if (toolsIndex < 0)
        {
            return yaml;
        }

        lines.InsertRange(toolsIndex,
        [
            "# MCP tools advertised for this repository. Comment out a line to hide that tool.",
            "# Removing the whole section advertises every tool. Writing tools also require",
            "# serve --allow-write, and indexing tools require serve --expose-indexing."
        ]);

        return string.Join(newLine, lines);
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
