using System.Collections.Concurrent;
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
public enum RepositoryIndexingPhase { Discovering, Indexing, Verifying, Publishing, Completed, FolderCompleted }

/// <summary>Progress counts refer to matching source files, with completion reported only after publication.</summary>
public sealed record RepositoryIndexingProgress(RepositoryIndexingPhase Phase, int CompletedFiles, int TotalFiles);

/// <summary>Summary of a fully published repository index, including how much analysis was reused.</summary>
public sealed record RepositoryIndexingResult(RepositoryIndexSnapshot Snapshot, int AnalyzedFiles, int ReusedFiles);

/// <summary>Counts for a verified run; both full and folder runs publish an MCP-queryable snapshot.</summary>
public sealed record RepositoryIndexRun(
    RepositoryState State, int AnalyzedFiles, int ReusedFiles, RepositoryIndexSnapshot? PublishedSnapshot);

/// <summary>Indexes selected files with bounded parallel summary generation and serialized SQLite staging.</summary>
public sealed class IndexRepository(
    IRepositoryStateProvider stateProvider,
    IRepositoryConfigurationLoader configurationLoader,
    IRepositoryFileEnumerator fileEnumerator,
    IRepositorySourceReader sourceReader,
    IIndexStore indexStore,
    IEnumerable<ISourceAnalyzer> analyzers,
    IEnumerable<ISourceReferenceAnalyzer>? referenceAnalyzers = null,
    IEnumerable<ISymbolSourceExtractor>? summaryExtractors = null,
    ISymbolSummaryGenerator? summaryGenerator = null,
    ISharedSummaryCache? sharedSummaryCache = null,
    IEnumerable<ISourceSyntaxDiagnostics>? syntaxAnalyzers = null)
{
    private readonly IReadOnlyList<ISourceAnalyzer> _analyzers = analyzers.ToArray();
    private readonly IReadOnlyDictionary<string, ISourceSyntaxDiagnostics> _syntaxAnalyzers =
        (syntaxAnalyzers ?? []).ToDictionary(item => item.AnalyzerId, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<ValidatedSourceKey, string> _validatedSourceHashes = new();
    private readonly IReadOnlyList<ISourceReferenceAnalyzer> _referenceAnalyzers = referenceAnalyzers?.ToArray() ?? [];
    private readonly IReadOnlyDictionary<string, ISymbolSourceExtractor> _summaryExtractors =
        summaryExtractors?.ToDictionary(item => item.AnalyzerId, StringComparer.OrdinalIgnoreCase)
        ?? new Dictionary<string, ISymbolSourceExtractor>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Verifies and publishes the checkout. Force bypasses reuse; requireValidSyntax rejects parser errors before publishing.</summary>
    public async Task<RepositoryIndexingResult> ExecuteAsync(
        string repositoryRoot,
        IProgress<RepositoryIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool force = false, bool requireValidSyntax = false)
    {
        var run = await RunAsync(repositoryRoot, null, progress, cancellationToken, force, requireValidSyntax);
        return new RepositoryIndexingResult(run.PublishedSnapshot!, run.AnalyzedFiles, run.ReusedFiles);
    }

    /// <summary>Indexes a checkout, or incrementally publishes one repository-relative folder.</summary>
    public async Task<RepositoryIndexRun> RunAsync(
        string repositoryRoot, string? folder, IProgress<RepositoryIndexingProgress>? progress = null,
        CancellationToken cancellationToken = default, bool force = false, bool requireValidSyntax = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = await stateProvider.GetStateAsync(repositoryRoot, cancellationToken);
        var root = state.Scope.RootPath;
        var configuration = await configurationLoader.LoadAsync(root, cancellationToken);
        var selectedAnalyzers = new ConfiguredSourceAnalyzers(configuration, _analyzers);

        if (configuration.SemanticIndexing.Llm.Enabled && summaryGenerator is null)
        {
            throw new InvalidOperationException("LLM summary indexing is enabled but no summary generator is registered.");
        }

        var fingerprint = Fingerprint(configuration, selectedAnalyzers);
        var candidates = Discover(root, configuration, selectedAnalyzers, folder, progress, cancellationToken);
        var useSharedSummaries = configuration.SharedSummaries && configuration.SemanticIndexing.Llm.Enabled;
        var summaryProfile = GetSummaryProfile(configuration);

        using var update = indexStore.BeginUpdate(
            state, fingerprint, configuration.SemanticIndexing.Language, cancellationToken,
            folder is null ? null : Path.GetRelativePath(root, Path.GetFullPath(folder, root)).Replace('\\', '/'));
        var reused = 0;
        var indexedFiles = new List<IndexedSourceFile>(candidates.Count);
        var pending = new Queue<PendingSummary>();
        var failedFiles = new List<string>();
        var consecutiveFailures = 0;
        var completed = 0;
        var llm = configuration.SemanticIndexing.Llm;
        var maxInFlight = llm.Enabled
            ? Math.Clamp(llm.Endpoints.Count * llm.MaxConcurrentRequestsPerEndpoint, 1, 32)
            : 1;
        using var summaryCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        progress?.Report(new(RepositoryIndexingPhase.Indexing, 0, candidates.Count));

        // Both TryReuse and AddFile are deliberately serialized. Only HTTP-backed summary work
        // is in flight concurrently, keeping SQLite transactions and file ordering deterministic.
        async Task CommitNextAsync()
        {
            var work = pending.Dequeue();

            if (work.Summaries is { } task)
            {
                try
                {
                    var described = await task;
                    update.AddFile(work.File, described, cancellationToken);
                    consecutiveFailures = 0;
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
                                                   exception is not OperationCanceledException)
                {
                    failedFiles.Add(work.File.RelativePath);
                    consecutiveFailures++;

                    if (consecutiveFailures >= llm.MaxConsecutiveFailedFiles)
                    {
                        throw new IOException(
                            $"LLM generation failed for {consecutiveFailures} consecutive files. " +
                            $"Stopping at '{work.File.RelativePath}' without publishing the index.", exception);
                    }
                }
            }
            else
            {
                // Reused files count as successful files for consecutive-failure accounting.
                consecutiveFailures = 0;
            }

            completed++;
            progress?.Report(new(RepositoryIndexingPhase.Indexing, completed, candidates.Count));
        }

        try
        {
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = await sourceReader.ReadAsync(root, candidate.RelativePath, cancellationToken);
                var adapter = candidate.Analyzer.Analyzer;
                var shared = useSharedSummaries && sharedSummaryCache is not null
                    ? await sharedSummaryCache.ReadAsync(root, candidate.RelativePath, cancellationToken)
                    : new SharedSummaryRead("", null);
                var file = new IndexedSourceFile(candidate.RelativePath, source.ContentHash, adapter.AnalyzerId,
                    adapter.IndexVersion, shared.Digest);
                indexedFiles.Add(file);

                if (requireValidSyntax)
                {
                    ValidateSyntax(state.Scope, file, source.Document, cancellationToken);
                }

                if (!force && update.TryReuse(file, cancellationToken))
                {
                    reused++;
                    pending.Enqueue(new PendingSummary(file, null));
                }
                else
                {
                    var symbols = await adapter.AnalyzeAsync(source.Document, cancellationToken);
                    var task = SummarizeSymbolsAsync(
                        source.Document, candidate.Analyzer, symbols, configuration.SemanticIndexing,
                        shared.File?.Matches(file, summaryProfile) == true ? shared.File : null,
                        summaryCancellation.Token);
                    pending.Enqueue(new PendingSummary(file, task));
                }

                if (pending.Count >= maxInFlight)
                {
                    await CommitNextAsync();
                }
            }

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await CommitNextAsync();
            }

            if (failedFiles.Count > 0)
            {
                throw new IOException(
                    $"LLM summaries failed for {failedFiles.Count} source file(s). " +
                    "Successful files remain staged for reuse; the published index was not replaced.");
            }
        }
        catch
        {
            // Stop work already scheduled beyond the file that crossed the failure threshold.
            summaryCancellation.Cancel();

            foreach (var work in pending)
            {
                if (work.Summaries is not { } task)
                {
                    continue;
                }

                try
                {
                    await task;
                }
                catch (Exception)
                {
                    // The original failure or cancellation is the relevant outcome.
                }
            }

            throw;
        }

        // Reference binding depends on other files and cannot be reused per file like declarations.
        // An unchanged snapshot can copy all resolved edges; otherwise rebuild them together.
        if (folder is null && (force || !update.TryReuseReferences(cancellationToken)))
        {
            await IndexReferencesAsync(root, candidates, indexedFiles, update, cancellationToken);
        }

        await VerifyFilesAsync(root, indexedFiles, progress, cancellationToken);

        if (useSharedSummaries && sharedSummaryCache is not null)
        {
            foreach (var file in indexedFiles)
            {
                var observed = await sharedSummaryCache.ReadAsync(root, file.RelativePath, cancellationToken);
                if (observed.Digest != file.SummaryCacheHash)
                {
                    throw new IOException($"Shared summary for '{file.RelativePath}' changed during indexing. Run index again.");
                }
            }
        }

        var finalCandidates = Discover(root, configuration, selectedAnalyzers, folder, null, cancellationToken);

        if (!candidates.Select(file => file.RelativePath).SequenceEqual(finalCandidates.Select(file => file.RelativePath)))
        {
            throw new IOException("Source files were added, deleted or renamed during indexing. Run index again.");
        }

        await VerifyCheckoutAsync(state, fingerprint, cancellationToken);
        progress?.Report(new(RepositoryIndexingPhase.Publishing, candidates.Count, candidates.Count));
        var snapshot = update.Publish(cancellationToken);
        progress?.Report(new(folder is null ? RepositoryIndexingPhase.Completed : RepositoryIndexingPhase.FolderCompleted,
            candidates.Count, candidates.Count));

        return new RepositoryIndexRun(state, candidates.Count - reused, reused, snapshot);
    }

    /// <summary>
    /// Parses each distinct content hash once per watcher lifetime, including files eligible for cached
    /// declarations. A previously persisted partial run must never bypass watch-mode syntax checks.
    /// The indexer's existing end-of-run hash verification detects edits made after parsing.
    /// </summary>
    private void ValidateSyntax(
        RepositoryScope scope, IndexedSourceFile file, SourceDocument document, CancellationToken cancellationToken)
    {
        var key = new ValidatedSourceKey(
            scope.RootPath, scope.BranchName, file.RelativePath, file.AnalyzerId);

        if (_validatedSourceHashes.TryGetValue(key, out var hash) &&
            string.Equals(hash, file.ContentHash, StringComparison.Ordinal))
        {
            return;
        }

        if (!_syntaxAnalyzers.TryGetValue(file.AnalyzerId, out var diagnostics))
        {
            throw new NotSupportedException(
                $"Watch mode requires syntax diagnostics for analyzer '{file.AnalyzerId}'.");
        }

        var firstError = diagnostics.GetFile(document, cancellationToken)
            .FirstOrDefault(diagnostic => string.Equals(diagnostic.Severity, "error", StringComparison.OrdinalIgnoreCase));

        if (firstError is not null)
        {
            throw new InvalidOperationException(
                $"Syntax error in '{file.RelativePath}' at {firstError.Location.Line}:{firstError.Location.Column} " +
                $"({firstError.Id}): {firstError.Message}");
        }

        _validatedSourceHashes[key] = file.ContentHash;
    }

    private async Task<IReadOnlyList<CodeSemantic4Llm.Domain.Symbols.SymbolDefinition>> SummarizeSymbolsAsync(
        SourceDocument document, ConfiguredSourceAnalyzer analyzer,
        IReadOnlyList<CodeSemantic4Llm.Domain.Symbols.SymbolDefinition> symbols,
        SemanticIndexingConfiguration settings, SharedSummaryFile? shared, CancellationToken cancellationToken)
    {
        if (!settings.Llm.Enabled)
        {
            return symbols;
        }

        symbols = symbols.Select(symbol => shared?.Find(symbol) is { } summary
            ? symbol with { Summary = summary } : symbol).ToArray();
        var pending = symbols.Where(symbol => shared?.Find(symbol) is null &&
            (settings.Llm.ForceGeneration || string.IsNullOrWhiteSpace(symbol.Summary))).ToArray();

        if (pending.Length == 0)
        {
            return symbols;
        }

        if (!_summaryExtractors.TryGetValue(analyzer.AnalyzerId, out var extractor))
        {
            throw new InvalidOperationException(
                $"LLM summary extraction is not available for analyzer '{analyzer.AnalyzerId}'.");
        }

        var sourceByLocation = extractor.Extract(document, pending, cancellationToken);
        var described = new List<CodeSemantic4Llm.Domain.Symbols.SymbolDefinition>(symbols.Count);

        foreach (var symbol in symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!sourceByLocation.TryGetValue(symbol.Location, out var snippet))
            {
                described.Add(symbol);
                continue;
            }

            var summary = await summaryGenerator!.SummarizeAsync(symbol, snippet, settings, cancellationToken);

            if (string.IsNullOrWhiteSpace(summary))
            {
                // Missing generated text must not discard source documentation or stop indexing.
                described.Add(symbol);
                continue;
            }

            // Only the generated summary is persisted. The source snippet stays transient.
            described.Add(symbol with { Summary = string.Join("\n", summary
                .Replace("\r", "", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(10)) });
        }

        return described;
    }

    private List<IndexingSource> Discover(
        string root, RepositoryIndexingConfiguration configuration, ConfiguredSourceAnalyzers analyzers,
        string? folder, IProgress<RepositoryIndexingProgress>? progress, CancellationToken cancellationToken)
    {
        var candidates = new List<IndexingSource>();
        progress?.Report(new(RepositoryIndexingPhase.Discovering, 0, 0));

        foreach (var path in fileEnumerator.EnumerateFiles(root, configuration.Exclusions, cancellationToken, folder))
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

    private async Task IndexReferencesAsync(
        string root, IReadOnlyList<IndexingSource> candidates, IReadOnlyList<IndexedSourceFile> files,
        IIndexUpdate update, CancellationToken cancellationToken)
    {
        foreach (var analyzer in _referenceAnalyzers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var documents = new List<SourceDocument>();

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];

                if (!string.Equals(candidate.Analyzer.Analyzer.AnalyzerId, analyzer.AnalyzerId, StringComparison.Ordinal))
                {
                    continue;
                }

                var observed = await sourceReader.ReadAsync(root, candidate.RelativePath, cancellationToken);

                if (observed.ContentHash != files[index].ContentHash)
                {
                    throw new IOException($"Source file '{candidate.RelativePath}' changed during reference indexing. Run index again.");
                }

                documents.Add(observed.Document);
            }

            var references = await analyzer.AnalyzeAsync(documents, cancellationToken);
            update.AddReferences(references, cancellationToken);
        }
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

    private string Fingerprint(RepositoryIndexingConfiguration configuration, ConfiguredSourceAnalyzers analyzers)
    {
        // ProjectName is display metadata; renaming it does not change derived declarations.
        // Inline API keys only authenticate requests; rotating one does not change derived content.
        var settings = JsonSerializer.Serialize(new
        {
            configuration.Version,
            configuration.SharedSummaries,
            configuration.SemanticIndexing.Language,
            Llm = new
            {
                configuration.SemanticIndexing.Llm.Enabled,
                configuration.SemanticIndexing.Llm.ForceGeneration,
                configuration.SemanticIndexing.Llm.Model,
                configuration.SemanticIndexing.Llm.ReasoningEffort,
                configuration.SemanticIndexing.Llm.MaxCompletionTokens,
                configuration.SemanticIndexing.Llm.SystemPrompt,
                GeneratorVersion = configuration.SemanticIndexing.Llm.Enabled
                    ? summaryGenerator?.CacheVersion : null,
                Extractors = configuration.SemanticIndexing.Llm.Enabled
                    ? _summaryExtractors.OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => new { item.Key, Version = item.Value.GetType().Assembly.ManifestModule.ModuleVersionId })
                    : null
            },
            configuration.Exclusions,
            ReferenceAnalyzers = _referenceAnalyzers.OrderBy(entry => entry.AnalyzerId, StringComparer.Ordinal)
                .Select(entry => new { entry.AnalyzerId, entry.IndexVersion }),
            Analyzers = analyzers.Entries.Select(entry => new
            {
                entry.AnalyzerId, entry.Analyzer.IndexVersion, Extensions = entry.FileExtensions.ToArray()
            })
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(settings)));
    }

    /// <summary>Matches an export to the exact settings used by a published local index.</summary>
    public string GetConfigurationFingerprint(RepositoryIndexingConfiguration configuration) =>
        Fingerprint(configuration, new ConfiguredSourceAnalyzers(configuration, _analyzers));

    /// <summary>Portable model, prompt and language identity; secrets never enter exported metadata.</summary>
    public SummaryProfile GetSummaryProfile(RepositoryIndexingConfiguration configuration) =>
        SummaryProfile.Create(configuration.SemanticIndexing, summaryGenerator?.CacheVersion ?? "unavailable");

    private sealed record PendingSummary(
        IndexedSourceFile File, Task<IReadOnlyList<CodeSemantic4Llm.Domain.Symbols.SymbolDefinition>>? Summaries);

    private sealed record IndexingSource(string RelativePath, ConfiguredSourceAnalyzer Analyzer);

    private readonly record struct ValidatedSourceKey(
        string RootPath, string BranchName, string RelativePath, string AnalyzerId);
}
