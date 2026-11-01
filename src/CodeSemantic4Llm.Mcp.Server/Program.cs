using CodeSemantic4Llm.Mcp.Server;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

var services = new ServiceCollection();
services.AddLogging(logging => logging
    .SetMinimumLevel(LogLevel.Warning)
    .AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace));
services.AddRepositoryServices();

var errorConsole = AnsiConsole.Create(new AnsiConsoleSettings
{
    Out = new AnsiConsoleOutput(Console.Error)
});
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await CommandLineApplication.RunAsync(
    args, services, AnsiConsole.Console, errorConsole, cancellation.Token);
