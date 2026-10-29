using CodeSemantic4Llm.Application.UseCases;
using Spectre.Console;
using Spectre.Console.Cli;

namespace CodeSemantic4Llm.Mcp.Server.Commands;

/// <summary>Creates repository YAML from parsed settings without starting an MCP session.</summary>
public sealed class RepositoryInitCommand(
    InitializeRepositoryConfiguration initializeRepositoryConfiguration,
    IAnsiConsole console) : AsyncCommand<RepositoryInitSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        RepositoryInitSettings settings,
        CancellationToken cancellationToken)
    {
        var configurationPath = await initializeRepositoryConfiguration.ExecuteAsync(
            settings.RepositoryRoot,
            settings.Language,
            settings.AnalyzerIds.Length == 0 ? null : settings.AnalyzerIds,
            cancellationToken);

        console.WriteLine($"Created repository configuration: {configurationPath}");
        return 0;
    }
}
