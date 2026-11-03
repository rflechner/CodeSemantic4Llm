using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Configures the CLI parser, generated help, command activation and stderr error reporting.</summary>
public static class CommandLineApplication
{
    /// <summary>
    /// Runs the CLI, starting the stdio MCP server when no command is supplied.
    /// Each run owns its service provider; supplied console instances are not global state.
    /// </summary>
    public static Task<int> RunAsync(
        string[] arguments,
        IServiceCollection services,
        IAnsiConsole output,
        IAnsiConsole error,
        CancellationToken cancellationToken = default)
    {
        services.AddSingleton(output);
        var application = new CommandApp<StartMcpServerCommand>(new ServiceCollectionTypeRegistrar(services));

        application.Configure(configuration =>
        {
            configuration.SetApplicationName("codesemantic4llm");
            configuration.Settings.Culture = CultureInfo.InvariantCulture;
            configuration.ConfigureConsole(output);
            // In 0.57.2, strict parsing retries failures with an internal command token,
            // which can become a missing option's value. Reject Spectre's unmapped arguments
            // explicitly instead, preserving its original missing-value diagnostics.
            configuration.Settings.StrictParsing = false;
            configuration.SetInterceptor(new UnmappedArgumentsInterceptor());
            configuration.SetExceptionHandler((exception, _) =>
            {
                // Paths and diagnostics are data, never Spectre markup.
                error.WriteLine($"Error: {exception.Message}");
                return exception is OperationCanceledException ? 130 : 1;
            });

            configuration.AddCommand<RepositoryInitCommand>("init")
                .WithDescription("Create repository YAML without overwriting an existing configuration.")
                .WithExample("init", "./repository", "--name", "MyProject", "--lang", "fr", "--analyzer", "dotnet-csharp");

            configuration.AddCommand<RepositoryPreviewCommand>("preview")
                .WithDescription("Preview discovered source files without reading or indexing source content.")
                .WithExample("preview", "./repository", "--max-files", "200");

            configuration.AddCommand<RepositoryIndexCommand>("index")
                .WithDescription("Index every configured source file into a persistent branch snapshot.")
                .WithExample("index", "./repository");

            configuration.AddCommand<ExportSummariesCommand>("cache-export")
                .WithDescription("Export current indexed summaries as versionable Markdown; never stages or commits.");
            configuration.AddCommand<SetupSummaryGitCommand>("setup-git")
                .WithDescription("Install a repository-local merge driver and the shared-summary attributes rule.");
            configuration.AddCommand<MergeSummaryCommand>("git-merge-summary")
                .WithDescription("Git merge driver: select one complete summary document using current/other policy.");
            configuration.AddCommand<WatchRepositoryCommand>("watch")
                .WithDescription("Reindex syntax-valid changes after the repository YAML debounce interval (15s by default).")
                .WithExample("watch", "./repository")
                .WithExample("watch", "./repository", "--debounce-seconds", "5");

            configuration.AddCommand<IndexDashboardCommand>("dashboard")
                .WithDescription("Explore all locally indexed repositories and branches in a read-only HTTP UI.")
                .WithExample("dashboard", "--port", "5080");

            configuration.AddCommand<ServeMcpServerCommand>("serve")
                .WithDescription("Start the stdio MCP server, optionally selecting a repository.")
                .WithExample("serve", "./repository", "--allow-write");
        });

        return application.RunAsync(arguments, cancellationToken);
    }
}
