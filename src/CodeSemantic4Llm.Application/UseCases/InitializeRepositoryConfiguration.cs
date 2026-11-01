using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Creates configuration with an optional project name, selected analyzers and semantic description language.</summary>
public sealed class InitializeRepositoryConfiguration(
    IRepositoryConfigurationWriter configurationWriter,
    IEnumerable<ISourceAnalyzer> analyzers)
{
    private const string DefaultAnalyzerId = "dotnet-csharp";

    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    /// <summary>
    /// Initializes an existing directory using an absolute or current-directory-relative path.
    /// Defaults to English and C# when no analyzer selection is supplied. Explicit selections
    /// replace that default, preserve first occurrence order, and must all be available.
    /// Analyzer extensions retain their defaults, without adding user exclusions.
    /// The optional project name is a display label and does not determine index storage paths.
    /// Every MCP tool is selected, so an operator only has to comment out the tools this
    /// repository should not advertise.
    /// </summary>
    public Task<string> ExecuteAsync(
        string repositoryRoot,
        string language = SemanticIndexingConfiguration.DefaultLanguage,
        IEnumerable<string>? analyzerIds = null,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var semanticIndexing = new SemanticIndexingConfiguration(language);
        var configuration = new RepositoryIndexingConfiguration(
            analyzerIds ?? [DefaultAnalyzerId], semanticIndexing: semanticIndexing, projectName: projectName);

        ValidateAnalyzerAvailability(configuration.AnalyzerIds);

        return configurationWriter.CreateAsync(repositoryRoot, configuration, cancellationToken);
    }

    private void ValidateAnalyzerAvailability(IReadOnlyList<string> analyzerIds)
    {
        foreach (var id in analyzerIds)
        {
            if (_analyzers.Any(analyzer => string.Equals(
                    analyzer.AnalyzerId, id, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var available = string.Join(", ", _analyzers
                .Select(analyzer => analyzer.AnalyzerId)
                .OrderBy(analyzerId => analyzerId, StringComparer.Ordinal));

            throw new RepositoryConfigurationException(
                $"Analyzer '{id}' is not installed or implemented. Available analyzers: {available}.");
        }
    }
}
