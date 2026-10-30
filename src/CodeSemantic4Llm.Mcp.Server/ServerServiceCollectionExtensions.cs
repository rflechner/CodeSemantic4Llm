using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Persistence.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace CodeSemantic4Llm.Mcp.Server;

/// <summary>Shares application and adapter registrations between local commands and the MCP host.</summary>
internal static class ServerServiceCollectionExtensions
{
    public static void AddRepositoryServices(this IServiceCollection services)
    {
        services.AddSingleton<ISourceAnalyzer, CSharpSourceAnalyzer>();
        services.AddSingleton<ISourceReferenceAnalyzer, CSharpReferenceAnalyzer>();
        services.AddSingleton<IMethodBodyEditor, CSharpMethodBodyEditor>();
        services.AddSingleton<IMethodSyntaxDiagnostics, CSharpMethodSyntaxDiagnostics>();
        services.AddSingleton<IGitBranchProvider, GitBranchProvider>();
        services.AddSingleton<IRepositoryStateProvider, GitRepositoryStateProvider>();
        services.AddSingleton<IRepositorySourceReader, RepositorySourceReader>();
        services.AddSingleton<IRepositorySourceWriter, RepositorySourceWriter>();
        services.AddSingleton(_ => IndexStoragePaths.FromEnvironment());
        services.AddSingleton<IIndexStore, SqliteIndexStore>();
        services.AddSingleton<IRepositoryConfigurationLoader, YamlRepositoryConfigurationLoader>();
        services.AddSingleton<IRepositoryConfigurationWriter, YamlRepositoryConfigurationWriter>();
        services.AddSingleton<IRepositoryFileEnumerator, FileSystemRepositoryFileEnumerator>();
        services.AddSingleton<AnalyzeSource>();
        services.AddSingleton<PreviewRepositoryIndex>();
        services.AddSingleton<InitializeRepositoryConfiguration>();
        services.AddSingleton<IndexRepository>();
        services.AddSingleton<FindSymbols>();
        services.AddSingleton<GetTypeMembers>();
        services.AddSingleton<MethodBodyOperations>();
        services.AddSingleton<FindUsages>();
    }
}
