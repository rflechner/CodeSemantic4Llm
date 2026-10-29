using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>Runs the production CLI parser with isolated, non-interactive output streams.</summary>
internal static class ConsoleCommandRunner
{
    public static async Task<ConsoleCommandResult> RunAsync(
        string[] arguments,
        IServiceCollection services,
        CancellationToken cancellationToken)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await CommandLineApplication.RunAsync(
            arguments, services, CreateConsole(output), CreateConsole(error), cancellationToken);
        return new ConsoleCommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static IAnsiConsole CreateConsole(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            Interactive = InteractionSupport.No
        });
        console.Profile.Width = 240;

        return console;
    }
}

/// <summary>Captured exit status and streams from a command invocation.</summary>
internal sealed record ConsoleCommandResult(int ExitCode, string Output, string Error);
