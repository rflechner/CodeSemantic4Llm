using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Analysis.TypeScript;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Llm;
using CodeSemantic4Llm.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CodeSemantic4Llm.Mcp.Server;

/// <summary>Shares application and adapter registrations between local commands and the MCP host.</summary>
internal static class ServerServiceCollectionExtensions
{
    public static void AddRepositoryServices(this IServiceCollection services)
    {
        services.AddSingleton<ISourceAnalyzer, CSharpSourceAnalyzer>();
        services.AddSingleton<ISourceAnalyzer, RazorSourceAnalyzer>();
        services.AddSingleton<ISourceAnalyzer, TypeScriptSourceAnalyzer>();
        services.AddSingleton<ISymbolSourceExtractor, CSharpSymbolSourceExtractor>();
        services.AddSingleton<ISymbolSourceExtractor, RazorSymbolSourceExtractor>();
        services.AddSingleton<ISymbolSourceExtractor, TypeScriptSymbolSourceExtractor>();
        services.AddHttpClient<ISymbolSummaryGenerator, OpenAiSymbolSummaryGenerator>(client =>
            client.Timeout = TimeSpan.FromMinutes(5));
        services.AddSingleton<ISourceReferenceAnalyzer, CSharpReferenceAnalyzer>();
        services.AddSingleton<IMethodBodyEditor, CSharpMethodBodyEditor>();
        services.AddSingleton<IMethodDeclarationEditor, CSharpMethodDeclarationEditor>();
        services.AddSingleton<IMethodSyntaxDiagnostics, CSharpMethodSyntaxDiagnostics>();
        services.AddSingleton<ISourceSyntaxDiagnostics, CSharpSourceSyntaxDiagnostics>();
        services.AddSingleton<ISourceSyntaxDiagnostics, TypeScriptSourceSyntaxDiagnostics>();
        services.AddSingleton<IGitBranchProvider, GitBranchProvider>();
        services.AddSingleton<IRepositoryStateProvider, GitRepositoryStateProvider>();
        services.AddSingleton<IRepositorySourceReader, RepositorySourceReader>();
        services.AddSingleton<IRepositorySourceWriter, RepositorySourceWriter>();
        services.AddSingleton(_ => IndexStoragePaths.FromEnvironment());
        services.AddSingleton<IIndexStore, SqliteIndexStore>();
        services.AddSingleton<IIndexDashboardReader, SqliteIndexDashboardReader>();
        services.AddSingleton<IRepositoryConfigurationLoader, YamlRepositoryConfigurationLoader>();
        services.AddSingleton<IRepositoryConfigurationWriter, YamlRepositoryConfigurationWriter>();
        services.AddSingleton<IRepositoryFileEnumerator, FileSystemRepositoryFileEnumerator>();
        services.AddSingleton<ISharedSummaryCodec, MarkdownSummaryCodec>();
        services.AddSingleton<ISharedSummaryCache, MarkdownSummaryCache>();
        services.AddSingleton<ISummaryExportReader, SqliteSummaryExportReader>();
        services.AddSingleton<ExportSharedSummaries>();
        services.AddSingleton<GitSummaryIntegration>();
        services.AddSingleton<RepositoryIndexWatcher>();
        services.AddSingleton<AnalyzeSource>();
        services.AddSingleton<PreviewRepositoryIndex>();
        services.AddSingleton<InitializeRepositoryConfiguration>();
        services.AddSingleton<IndexRepository>();
        services.AddSingleton<FindSymbols>();
        services.AddSingleton<SearchCode>();
        services.AddSingleton<GetTypeMembers>();
        services.AddSingleton<MethodBodyOperations>();
        services.AddSingleton<CurrentMethodOperations>();
        services.AddSingleton<MethodBodyPages>();
        services.AddSingleton<SelectCodeDeclaration>();
        services.AddSingleton<GetSourceDiagnostics>();
        services.AddSingleton<FindUsages>();
    }
}
