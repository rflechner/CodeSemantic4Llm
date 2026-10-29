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

if (args.FirstOrDefault() is "init" or "preview" or "--help" or "-h")
{
    var services = new ServiceCollection();
    AddApplicationServices(services);
    services.AddSingleton<RepositoryInitCommand>();
    services.AddSingleton<RepositoryPreviewCommand>();

    using var provider = services.BuildServiceProvider();
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    if (args[0] == "init")
    {
        var init = provider.GetRequiredService<RepositoryInitCommand>();
        return await init.ExecuteAsync(args[1..], Console.Out, Console.Error, cancellation.Token);
    }

    if (args[0] is "--help" or "-h")
    {
        var init = provider.GetRequiredService<RepositoryInitCommand>();
        await init.ExecuteAsync(["--help"], Console.Out, Console.Error, cancellation.Token);
        await Console.Out.WriteLineAsync("Run without a command to start the stdio MCP server.");
    }

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
    services.AddSingleton<IRepositoryConfigurationWriter, YamlRepositoryConfigurationWriter>();
    services.AddSingleton<IRepositoryFileEnumerator, FileSystemRepositoryFileEnumerator>();
    services.AddSingleton<AnalyzeSource>();
    services.AddSingleton<PreviewRepositoryIndex>();
    services.AddSingleton<InitializeRepositoryConfiguration>();
}
