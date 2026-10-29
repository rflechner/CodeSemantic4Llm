using System.Collections.ObjectModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>One source file selected for indexing with a configured language analyzer.</summary>
public sealed record IndexingCandidate(string RelativePath, string AnalyzerId);

/// <summary>A bounded, read-only preview of the effective per-repository indexing rules.</summary>
public sealed record RepositoryIndexingPreview(
    string RepositoryRoot,
    string ConfigurationFile,
    IReadOnlyList<string> EnabledAnalyzers,
    IReadOnlyDictionary<string, SourceFileExtensions> EffectiveFileExtensions,
    IReadOnlyList<string> EffectiveExclusions,
    string SemanticIndexingLanguage,
    IReadOnlyList<IndexingCandidate> Files,
    bool HasMore);

/// <summary>
/// Applies per-repository YAML settings when discovering indexable source paths.
/// Does not read source content or write index records.
/// </summary>
public sealed class PreviewRepositoryIndex(
    IRepositoryConfigurationLoader configurationLoader,
    IRepositoryFileEnumerator fileEnumerator,
    IEnumerable<ISourceAnalyzer> analyzers)
{
    public const int DefaultMaxFiles = 50;

    public const int MaximumMaxFiles = 200;

    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    public async Task<RepositoryIndexingPreview> ExecuteAsync(
        string repositoryRoot,
        int maxFiles = DefaultMaxFiles,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);

        if (maxFiles > MaximumMaxFiles)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxFiles), $"The maximum preview size is {MaximumMaxFiles} files.");
        }

        var root = Path.GetFullPath(repositoryRoot);
        var config = await configurationLoader.LoadAsync(root, cancellationToken);
        var selectedAnalyzers = ResolveAnalyzers(config);
        var selected = new List<IndexingCandidate>();

        foreach (var path in fileEnumerator.EnumerateFiles(root, config.Exclusions, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var analyzer = selectedAnalyzers.FirstOrDefault(item => item.FileExtensions.Matches(path));

            if (analyzer is null)
            {
                continue;
            }

            if (selected.Count == maxFiles)
            {
                return CreatePreview(true);
            }

            selected.Add(new IndexingCandidate(path, analyzer.AnalyzerId));
        }

        return CreatePreview(false);

        RepositoryIndexingPreview CreatePreview(bool hasMore) => new(
            root,
            RepositoryIndexingConfiguration.FileName,
            config.AnalyzerIds,
            new ReadOnlyDictionary<string, SourceFileExtensions>(
                selectedAnalyzers.ToDictionary(analyzer => analyzer.AnalyzerId, analyzer => analyzer.FileExtensions)),
            config.Exclusions,
            config.SemanticIndexing.Language,
            selected.AsReadOnly(),
            hasMore);
    }

    private IReadOnlyList<ConfiguredAnalyzer> ResolveAnalyzers(RepositoryIndexingConfiguration config)
    {
        var selected = new List<ConfiguredAnalyzer>();

        foreach (var id in config.AnalyzerIds)
        {
            var analyzer = _analyzers.FirstOrDefault(candidate =>
                string.Equals(candidate.AnalyzerId, id, StringComparison.OrdinalIgnoreCase));

            if (analyzer is null)
            {
                var available = string.Join(", ", _analyzers.Select(a => a.AnalyzerId).OrderBy(s => s));
                throw new RepositoryConfigurationException(
                    $"Analyzer '{id}' is not installed or implemented. Available analyzers: {available}.");
            }

            var extensions = config.FileExtensions.TryGetValue(id, out var configuredExtensions)
                ? configuredExtensions
                : analyzer.DefaultFileExtensions;

            selected.Add(new ConfiguredAnalyzer(id, extensions));
        }

        return selected;
    }

    private sealed record ConfiguredAnalyzer(string AnalyzerId, SourceFileExtensions FileExtensions);
}
