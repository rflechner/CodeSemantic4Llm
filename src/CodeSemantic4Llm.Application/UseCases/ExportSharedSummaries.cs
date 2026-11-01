using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Domain.Source;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Explicitly publishes current local descriptions to reviewable Markdown; never stages or commits Git files.</summary>
public sealed class ExportSharedSummaries(
    IRepositoryStateProvider stateProvider, IRepositoryConfigurationLoader configurationLoader,
    IRepositorySourceReader sourceReader, ISummaryExportReader exportReader,
    ISharedSummaryCache cache, IndexRepository indexer)
{
    public async Task<int> ExecuteAsync(string root, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var state = await stateProvider.GetStateAsync(root, cancellationToken);
        root = state.Scope.RootPath;
        var configuration = await configurationLoader.LoadAsync(root, cancellationToken);
        if (!configuration.SharedSummaries || !configuration.SemanticIndexing.Llm.Enabled)
        {
            throw new InvalidOperationException("Export requires sharedSummaries: true and enabled LLM indexing in repository YAML.");
        }

        var fingerprint = indexer.GetConfigurationFingerprint(configuration);
        var files = exportReader.Read(state, fingerprint, indexer.GetSummaryProfile(configuration), cancellationToken);
        // Check all sources before making any change, then recheck each immediately before its atomic write.
        foreach (var file in files)
        {
            await VerifySourceAsync(root, file, cancellationToken);
        }

        var written = 0;
        foreach (var file in files)
        {
            var observedState = await stateProvider.GetStateAsync(root, cancellationToken);
            var observedConfig = await configurationLoader.LoadAsync(root, cancellationToken);
            if (observedState != state || indexer.GetConfigurationFingerprint(observedConfig) != fingerprint)
            {
                throw new IOException("Checkout or configuration changed during export. Review exported files and retry.");
            }

            await VerifySourceAsync(root, file, cancellationToken);
            if (await cache.WriteAsync(root, file, overwrite, cancellationToken))
            {
                written++;
            }
        }

        return written;
    }

    private async Task VerifySourceAsync(string root, SharedSummaryFile file, CancellationToken cancellationToken)
    {
        var source = await sourceReader.ReadAsync(root, file.Source.RelativePath, cancellationToken);
        if (source.ContentHash != file.Source.ContentHash)
        {
            throw new IOException($"Source '{file.Source.RelativePath}' changed since indexing. Run index before export.");
        }
    }
}
