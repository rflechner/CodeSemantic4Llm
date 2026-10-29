using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.Configuration;
using CodeSemantic4Llm.Domain.Configuration;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Stages in an index run; Completed is emitted only after the transaction commits.</summary>
public enum RepositoryIndexingPhase { Discovering, Indexing, Verifying, Publishing, Completed }

/// <summary>Progress counts refer to matching source files, with completion reported only after publication.</summary>
public sealed record RepositoryIndexingProgress(RepositoryIndexingPhase Phase, int CompletedFiles, int TotalFiles);

/// <summary>Summary of a fully published repository index, including how much analysis was reused.</summary>
public sealed record RepositoryIndexingResult(RepositoryIndexSnapshot Snapshot, int AnalyzedFiles, int ReusedFiles);

/// <summary>Indexes all selected files, one at a time, into an atomic branch-specific generation.</summary>
public sealed class IndexRepository(
    IRepositoryStateProvider stateProvider,
    IRepositoryConfigurationLoader configurationLoader,
    IRepositoryFileEnumerator fileEnumerator,
    IRepositorySourceReader sourceReader,
    IIndexStore indexStore,
    IEnumerable<ISourceAnalyzer> analyzers)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();

    public async Task<RepositoryIndexingResult> ExecuteAsync(
        string repositoryRoot,
        IProgress<RepositoryIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var root = state.Scope.RootPath;
        var configuration = await configurationLoader.LoadAsync(root, cancellationToken);
        var selectedAnalyzers = new ConfiguredSourceAnalyzers(configuration, _analyzers);
        var fingerprint = Fingerprint(configuration, selectedAnalyzers);
        var candidates = Discover(root, configuration, selectedAnalyzers, progress, cancellationToken);

        using var update = indexStore.BeginUpdate(
            state, fingerprint, configuration.SemanticIndexing.Language, cancellationToken);
        var reused = 0;
        var indexedFiles = new List<IndexedSourceFile>(candidates.Count);
        progress?.Report(new(RepositoryIndexingPhase.Indexing, 0, candidates.Count));

        for (var index = 0; index < candidates.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidates[index];
            var source = await sourceReader.ReadAsync(root, candidate.RelativePath, cancellationToken);
            var adapter = candidate.Analyzer.Analyzer;
            var file = new IndexedSourceFile(candidate.RelativePath, source.ContentHash, adapter.AnalyzerId, adapter.IndexVersion);
            indexedFiles.Add(file);

            if (update.TryReuse(file, cancellationToken))
            {
                reused++;
            }
            else
            {
                var symbols = await adapter.AnalyzeAsync(source.Document, cancellationToken);
                update.AddFile(file, symbols, cancellationToken);
            }

            progress?.Report(new(RepositoryIndexingPhase.Indexing, index + 1, candidates.Count));
        }

        await VerifyFilesAsync(root, indexedFiles, progress, cancellationToken);
        var finalCandidates = Discover(root, configuration, selectedAnalyzers, null, cancellationToken);

        if (!candidates.Select(file => file.RelativePath).SequenceEqual(finalCandidates.Select(file => file.RelativePath)))
        {
            throw new IOException("Source files were added, deleted or renamed during indexing. Run index again.");
        }

        progress?.Report(new(RepositoryIndexingPhase.Publishing, candidates.Count, candidates.Count));
        await VerifyCheckoutAsync(state, fingerprint, cancellationToken);
        var snapshot = update.Publish(cancellationToken);
        progress?.Report(new(RepositoryIndexingPhase.Completed, candidates.Count, candidates.Count));

        return new RepositoryIndexingResult(snapshot, candidates.Count - reused, reused);
    }

    private List<IndexingSource> Discover(
        string root, RepositoryIndexingConfiguration configuration, ConfiguredSourceAnalyzers analyzers,
        IProgress<RepositoryIndexingProgress>? progress, CancellationToken cancellationToken)
    {
        var candidates = new List<IndexingSource>();
        progress?.Report(new(RepositoryIndexingPhase.Discovering, 0, 0));

        foreach (var path in fileEnumerator.EnumerateFiles(root, configuration.Exclusions, cancellationToken))
        {
            var analyzer = analyzers.Select(path);

            if (analyzer is not null)
            {
                candidates.Add(new IndexingSource(path, analyzer));
                progress?.Report(new(RepositoryIndexingPhase.Discovering, 0, candidates.Count));
            }
        }

        return candidates;
    }

    private async Task VerifyFilesAsync(
        string root, IReadOnlyList<IndexedSourceFile> files,
        IProgress<RepositoryIndexingProgress>? progress, CancellationToken cancellationToken)
    {
        progress?.Report(new(RepositoryIndexingPhase.Verifying, 0, files.Count));

        for (var index = 0; index < files.Count; index++)
        {
            var file = files[index];
            var observed = await sourceReader.ReadAsync(root, file.RelativePath, cancellationToken);

            if (observed.ContentHash != file.ContentHash)
            {
                throw new IOException($"Source file '{file.RelativePath}' changed during indexing. Run index again.");
            }

            progress?.Report(new(RepositoryIndexingPhase.Verifying, index + 1, files.Count));
        }
    }

    private async Task VerifyCheckoutAsync(RepositoryState expected, string fingerprint, CancellationToken cancellationToken)
    {
        var observed = await stateProvider.GetStateAsync(expected.Scope.RootPath, cancellationToken);
        var configuration = await configurationLoader.LoadAsync(expected.Scope.RootPath, cancellationToken);
        var selected = new ConfiguredSourceAnalyzers(configuration, _analyzers);

        if (observed != expected || Fingerprint(configuration, selected) != fingerprint)
        {
            throw new InvalidOperationException("The Git checkout or indexing configuration changed during indexing. Run index again.");
        }
    }

    private static string Fingerprint(RepositoryIndexingConfiguration configuration, ConfiguredSourceAnalyzers analyzers)
    {
        // ProjectName is display metadata; renaming it does not change derived declarations.
        var settings = JsonSerializer.Serialize(new
        {
            configuration.Version,
            configuration.SemanticIndexing.Language,
            configuration.Exclusions,
            Analyzers = analyzers.Entries.Select(entry => new
            {
                entry.AnalyzerId, entry.Analyzer.IndexVersion, Extensions = entry.FileExtensions.ToArray()
            })
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings)));
    }

    private sealed record IndexingSource(string RelativePath, ConfiguredSourceAnalyzer Analyzer);
}
