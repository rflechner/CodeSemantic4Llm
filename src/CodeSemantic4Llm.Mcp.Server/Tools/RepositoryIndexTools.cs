using System.ComponentModel;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using CodeSemantic4Llm.Persistence.Sqlite;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Exposes operator index status and provides direct indexed queries for application callers.</summary>
[McpServerToolType]
public sealed class RepositoryIndexTools(
    IConfiguration configuration, IRepositoryStateProvider stateProvider, IIndexStore indexStore,
    IndexStoragePaths storagePaths, FindSymbols findSymbols, GetTypeMembers getTypeMembers,
    FindUsages findUsages, SearchCode searchCode)
{
    [McpServerTool, Description("Reports the selected repository, Git branch, YAML and SQLite cache paths, and whether a published snapshot exists. Does not reindex or verify working-tree freshness.")]
    public async Task<RepositoryIndexStatus> GetRepositoryIndexStatusAsync(CancellationToken cancellationToken = default)
    {
        var state = await stateProvider.GetStateAsync(GetRepositoryRoot(), cancellationToken);
        var databasePath = storagePaths.GetDatabasePath(state.Scope.RootPath);
        var snapshot = indexStore.GetSnapshot(state.Scope, cancellationToken);
        return new RepositoryIndexStatus(
            state, snapshot, snapshot is not null,
            Path.Combine(state.Scope.RootPath, "codesemantic4llm.yaml"),
            storagePaths.Root, databasePath, File.Exists(databasePath), Environment.CurrentDirectory);
    }

    /// <summary>Queries the published snapshot directly; MCP search uses the source-addressed presentation.</summary>
    public Task<SymbolSearchResult> FindSymbolsAsync(
        string name, [Description("Maximum results, from 1 to 200.")] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        findSymbols.ExecuteAsync(GetRepositoryRoot(), name, maxResults, cancellationToken);

    /// <summary>Searches the published snapshot directly; MCP search uses the source-addressed presentation.</summary>
    public Task<SymbolSearchResult> SearchCodeAsync(
        [Description("Natural-language question or search keywords.")] string query,
        [Description("Optional exact symbol kind, for example method or class.")] string? kind = null,
        [Description("Maximum ranked results, from 1 to 200.")] int maxResults = 10,
        CancellationToken cancellationToken = default) =>
        searchCode.ExecuteAsync(GetRepositoryRoot(), query, kind, maxResults, cancellationToken);

    /// <summary>Queries members against an explicit snapshot for direct application callers.</summary>
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

    /// <summary>Queries references against an explicit snapshot for direct application callers.</summary>
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
public sealed record RepositoryIndexStatus(
    RepositoryState Checkout, RepositoryIndexSnapshot? Snapshot,
    bool IndexSnapshotExists, string ConfigurationPath, string IndexRoot, string IndexDatabasePath,
    bool IndexDatabaseExists, string WorkingDirectory);
