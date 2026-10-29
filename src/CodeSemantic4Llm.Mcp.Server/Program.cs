using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

// Stdio is reserved for MCP protocol messages. Human-readable logs go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<ISourceAnalyzer, CSharpSourceAnalyzer>();
builder.Services.AddSingleton<IGitBranchProvider, GitBranchProvider>();
builder.Services.AddSingleton<IRepositoryConfigurationLoader, YamlRepositoryConfigurationLoader>();
builder.Services.AddSingleton<IRepositoryFileEnumerator, FileSystemRepositoryFileEnumerator>();
builder.Services.AddSingleton<AnalyzeSource>();
builder.Services.AddSingleton<PreviewRepositoryIndex>();

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
