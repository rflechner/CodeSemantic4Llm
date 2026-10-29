using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.Configuration;

/// <summary>Resolves enabled adapters and their extension overrides in the configured priority order.</summary>
public sealed class ConfiguredSourceAnalyzers
{
    public ConfiguredSourceAnalyzers(RepositoryIndexingConfiguration configuration, IEnumerable<ISourceAnalyzer> available)
    {
        var installed = available.ToArray();
        Entries = Array.AsReadOnly(configuration.AnalyzerIds.Select(id => Resolve(id, configuration, installed)).ToArray());
    }

    public IReadOnlyList<ConfiguredSourceAnalyzer> Entries { get; }

    public ConfiguredSourceAnalyzer? Select(string relativePath) =>
        Entries.FirstOrDefault(analyzer => analyzer.FileExtensions.Matches(relativePath));

    private static ConfiguredSourceAnalyzer Resolve(
        string id, RepositoryIndexingConfiguration configuration, IReadOnlyList<ISourceAnalyzer> installed)
    {
        var analyzer = installed.FirstOrDefault(candidate =>
            string.Equals(candidate.AnalyzerId, id, StringComparison.OrdinalIgnoreCase));

        if (analyzer is null)
        {
            var names = string.Join(", ", installed.Select(item => item.AnalyzerId).Order(StringComparer.Ordinal));
            throw new RepositoryConfigurationException(
                $"Analyzer '{id}' is not installed or implemented. Available analyzers: {names}.");
        }

        var extensions = configuration.FileExtensions.TryGetValue(id, out var configured)
            ? configured : analyzer.DefaultFileExtensions;

        return new ConfiguredSourceAnalyzer(analyzer, extensions);
    }
}

/// <summary>An enabled adapter and the effective extension selection for this repository.</summary>
public sealed record ConfiguredSourceAnalyzer(ISourceAnalyzer Analyzer, SourceFileExtensions FileExtensions)
{
    public string AnalyzerId => Analyzer.AnalyzerId;
}
