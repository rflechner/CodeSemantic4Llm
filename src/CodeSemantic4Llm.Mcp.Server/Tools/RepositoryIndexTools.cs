using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Repositories;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Reads published indexes for the operator-selected checkout; never triggers indexing.</summary>
[McpServerToolType]
public sealed class RepositoryIndexTools(
    IConfiguration configuration, IRepositoryStateProvider stateProvider, IIndexStore indexStore, FindSymbols findSymbols)
{
    [McpServerTool, Description("Returns the saved index snapshot and observed Git state. This does not verify working-tree freshness.")]
    public async Task<RepositoryIndexStatus> GetRepositoryIndexStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateProvider.GetStateAsync(GetRepositoryRoot(), cancellationToken);
        return new RepositoryIndexStatus(state, indexStore.GetSnapshot(state.Scope, cancellationToken));
    }

    [McpServerTool, Description("Finds declarations by exact, case-sensitive simple name in the saved branch snapshot. Returns locations and snapshot identity, not source text. Run index again after edits.")]
    public Task<SymbolSearchResult> FindSymbolsAsync(
        string name, [Description("Maximum results, from 1 to 200.")] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        findSymbols.ExecuteAsync(GetRepositoryRoot(), name, maxResults, cancellationToken);

    private string GetRepositoryRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException("Set CODESEMANTIC4LLM_REPOSITORY_ROOT before querying repository indexes.");
}

/// <summary>Snapshot metadata alongside observed Git state; neither implies that source files remain unchanged.</summary>
public sealed record RepositoryIndexStatus(RepositoryState Checkout, RepositoryIndexSnapshot? Snapshot);
