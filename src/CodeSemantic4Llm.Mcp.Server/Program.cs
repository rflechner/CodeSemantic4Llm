using CodeSemantic4Llm.Analysis.CSharp;
using CodeSemantic4Llm.Application.Abstractions;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Configuration.Yaml;
using CodeSemantic4Llm.Infrastructure.Workspace;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

if (args.FirstOrDefault() is "preview" or "--help" or "-h")
{
    var services = new ServiceCollection();
    AddApplicationServices(services);
    services.AddSingleton<RepositoryPreviewCommand>();

    using var provider = services.BuildServiceProvider();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    var command = provider.GetRequiredService<RepositoryPreviewCommand>();
    var commandArguments = args[0] == "preview" ? args[1..] : ["--help"];

    return await command.ExecuteAsync(commandArguments, Console.Out, Console.Error, cancellation.Token);
}

var builder = Host.CreateApplicationBuilder(args);

// Stdio is reserved for MCP protocol messages. Human-readable logs go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

AddApplicationServices(builder.Services);

builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
return 0;

static void AddApplicationServices(IServiceCollection services)
{
    services.AddSingleton<ISourceAnalyzer, CSharpSourceAnalyzer>();
    services.AddSingleton<IGitBranchProvider, GitBranchProvider>();
    services.AddSingleton<IRepositoryConfigurationLoader, YamlRepositoryConfigurationLoader>();
    services.AddSingleton<IRepositoryFileEnumerator, FileSystemRepositoryFileEnumerator>();
    services.AddSingleton<AnalyzeSource>();
    services.AddSingleton<PreviewRepositoryIndex>();
}
