using System.Diagnostics;
using System.Text.Json;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests.Steps;

/// <summary>Verifies the production command router and the executable's default MCP transport.</summary>
[Binding]
public sealed class ConsoleCommandRoutingSteps
{
    private ConsoleCommandResult? _result;
    private string? _protocolResponse;

    [When("I request application help with \"(.*)\"")]
    [When("I invoke the application with \"(.*)\"")]
    public async Task WhenInvokeApplication(string argument)
    {
        _result = await ConsoleCommandRunner.RunAsync(
            [argument], new ServiceCollection(), TestContext.Current.CancellationToken);
    }

    [Then("application help lists init, preview and the default MCP server")]
    public void ThenApplicationHelpListsCommands()
    {
        Assert.NotNull(_result);
        Assert.Equal(0, _result.ExitCode);
        Assert.Empty(_result.Error);
        Assert.Contains("init", _result.Output);
        Assert.Contains("preview", _result.Output);
        Assert.Contains("stdio MCP server", _result.Output);
    }

    [Then("the application reports a console error containing \"(.*)\"")]
    public void ThenApplicationReportsError(string expected)
    {
        Assert.NotNull(_result);
        Assert.Equal(1, _result.ExitCode);
        Assert.Empty(_result.Output);
        Assert.Contains(expected, _result.Error);
    }

    [When("I start the application without arguments and initialize an MCP session")]
    public async Task WhenStartMcpServer()
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        startInfo.ArgumentList.Add(typeof(StartMcpServerCommand).Assembly.Location);

        using var process = Process.Start(startInfo)!;
        var errors = process.StandardError.ReadToEndAsync();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await process.StandardInput.WriteLineAsync(
                """
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"functional-test","version":"1.0"}}}
                """);
            await process.StandardInput.FlushAsync(timeout.Token);
            _protocolResponse = await process.StandardOutput.ReadLineAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        }

        Assert.True(_protocolResponse is not null, await errors);
    }

    [Then("the first stdout line is a successful MCP initialization response")]
    public void ThenFirstOutputIsMcpResponse()
    {
        Assert.NotNull(_protocolResponse);
        using var response = JsonDocument.Parse(_protocolResponse);
        var message = response.RootElement;

        Assert.Equal("2.0", message.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, message.GetProperty("id").GetInt32());
        Assert.False(message.TryGetProperty("error", out _));
        Assert.True(message.GetProperty("result").TryGetProperty("serverInfo", out _));
    }
}
