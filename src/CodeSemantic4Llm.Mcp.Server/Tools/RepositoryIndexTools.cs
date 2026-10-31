using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Reads published indexes for the operator-selected checkout; never triggers indexing.</summary>
[McpServerToolType]
public sealed class RepositoryIndexTools(
    IConfiguration configuration, IRepositoryStateProvider stateProvider, IIndexStore indexStore,
    FindSymbols findSymbols, GetTypeMembers getTypeMembers, FindUsages findUsages,
    SearchCode searchCode)
{
    [McpServerTool, Description("Returns the saved index snapshot and observed Git state. This does not verify working-tree freshness.")]
    public async Task<RepositoryIndexStatus> GetRepositoryIndexStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateProvider.GetStateAsync(GetRepositoryRoot(), cancellationToken);
        return new RepositoryIndexStatus(state, indexStore.GetSnapshot(state.Scope, cancellationToken));
    }

    [McpServerTool, Description("Finds declarations by exact, case-sensitive simple name in the saved branch snapshot. Returns locations and snapshot identity, not source text. To list a class's methods, find the class here, then call get_type_members with its location and snapshotId. Run index again after edits.")]
    public Task<SymbolSearchResult> FindSymbolsAsync(
        string name, [Description("Maximum results, from 1 to 200.")] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        findSymbols.ExecuteAsync(GetRepositoryRoot(), name, maxResults, cancellationToken);

    [McpServerTool, Description("Finds source declarations by querying saved names and optional LLM summaries with SQLite FTS5/BM25. Returns ranked locations and descriptions from the selected published branch. Does not access source files or call an LLM. Reindex after changing code or upgrading old caches.")]
    public Task<SymbolSearchResult> SearchCodeAsync(
        [Description("Natural-language question or search keywords.")] string query,
        [Description("Optional exact symbol kind, for example method or class.")] string? kind = null,
        [Description("Maximum ranked results, from 1 to 200.")] int maxResults = 10,
        CancellationToken cancellationToken = default) =>
        searchCode.ExecuteAsync(GetRepositoryRoot(), query, kind, maxResults, cancellationToken);

    [McpServerTool, Description("Lists direct members of one indexed type declaration. First use find_symbols for the type, then pass its location and snapshot.snapshotId. Set kind to method for methods or omit it for all members. Does not include inherited members or combine partial declarations; query each returned type declaration separately. Follow nextOffset for more results.")]
    public Task<TypeMemberPage> GetTypeMembersAsync(
        [Description("Type declaration's location.relativePath from find_symbols.")] string relativePath,
        [Description("Type declaration's one-based location.line.")] int line,
        [Description("Type declaration's one-based location.column.")] int column,
        [Description("snapshot.snapshotId returned by find_symbols; prevents mixing index generations.")] long snapshotId,
        [Description("Optional exact kind: method, constructor, property, field, event, class, struct, interface, record, enum, enum-member, delegate.")] string? kind = null,
        [Description("Maximum results, from 1 to 200.")] int maxResults = 50,
        [Description("Start at 0; use nextOffset from the preceding page to continue.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        getTypeMembers.ExecuteAsync(GetRepositoryRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, kind, maxResults, offset, cancellationToken);

    [McpServerTool, Description("Finds references to one exact indexed method or type using Roslyn-resolved symbol identities. First call find_symbols, then pass the declaration's location and snapshot.snapshotId. Returns repository-relative reference positions, not source text. Results are paginated with nextOffset. C# project references are not yet evaluated; unresolved references are omitted. Run index again after source edits.")]
    public Task<SymbolUsagePage> FindUsagesAsync(
        [Description("Declaration's location.relativePath from find_symbols.")] string relativePath,
        [Description("Declaration's one-based location.line.")] int line,
        [Description("Declaration's one-based location.column.")] int column,
        [Description("snapshot.snapshotId returned by find_symbols.")] long snapshotId,
        [Description("Maximum references per page, from 1 to 200.")] int maxResults = 50,
        [Description("Start at 0; use nextOffset to read subsequent pages.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        findUsages.ExecuteAsync(GetRepositoryRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, maxResults, offset, cancellationToken);

    private string GetRepositoryRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException("Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT before querying repository indexes.");
}

/// <summary>Snapshot metadata alongside observed Git state; neither implies that source files remain unchanged.</summary>
public sealed record RepositoryIndexStatus(RepositoryState Checkout, RepositoryIndexSnapshot? Snapshot);
