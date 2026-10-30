using System.Diagnostics;
using System.Text.Json;
using CodeSemantic4Llm.Mcp.Server.Commands;
using Xunit;

namespace CodeSemantic4Llm.FunctionalTests;

/// <summary>A real stdio MCP client for repository workflows, with bounded lifetime and captured stderr.</summary>
internal sealed class McpCodeClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task<string> _errors;
    private readonly CancellationTokenSource _timeout;
    private int _requestId = 1;

    private McpCodeClient(Process process, CancellationToken cancellationToken)
    {
        _process = process;
        _errors = process.StandardError.ReadToEndAsync();
        _timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timeout.CancelAfter(TimeSpan.FromSeconds(30));
    }

    public static async Task<McpCodeClient> StartAsync(
        string root, string cacheRoot, bool allowWrite, bool exposeIndexing, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, WorkingDirectory = root
        };
        start.ArgumentList.Add(typeof(StartMcpServerCommand).Assembly.Location);
        start.ArgumentList.Add("serve");
        start.ArgumentList.Add(root);
        if (allowWrite)
        {
            start.ArgumentList.Add("--allow-write");
        }

        if (exposeIndexing)
        {
            start.ArgumentList.Add("--expose-indexing");
        }

        start.Environment["CODESEMANTIC4LLM_INDEX_ROOT"] = cacheRoot;
        var client = new McpCodeClient(Process.Start(start)!, cancellationToken);

        try
        {
            await client.SendAsync("""
                {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"code-tools-test","version":"1.0"}}}
                """);
            await client.ReceiveAsync(1);
            await client.SendAsync("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task<JsonElement> CallAsync(string name, object arguments)
    {
        var id = ++_requestId;
        await SendAsync(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id, method = "tools/call", @params = new { name, arguments }
        }));
        return await ReceiveAsync(id);
    }

    public static JsonElement ReadContent(JsonElement response)
    {
        Assert.False(response.TryGetProperty("isError", out var error) && error.GetBoolean(), response.ToString());
        using var content = JsonDocument.Parse(response.GetProperty("content")[0].GetProperty("text").GetString()!);
        return content.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _process.WaitForExitAsync(CancellationToken.None);
        await _errors;
        _process.Dispose();
        _timeout.Dispose();
    }

    private async Task SendAsync(string message)
    {
        await _process.StandardInput.WriteLineAsync(message.AsMemory(), _timeout.Token);
        await _process.StandardInput.FlushAsync(_timeout.Token);
    }

    private async Task<JsonElement> ReceiveAsync(int id)
    {
        var line = await _process.StandardOutput.ReadLineAsync(_timeout.Token);
        Assert.NotNull(line);
        using var response = JsonDocument.Parse(line);
        Assert.Equal(id, response.RootElement.GetProperty("id").GetInt32());
        Assert.False(response.RootElement.TryGetProperty("error", out _), line);
        return response.RootElement.GetProperty("result").Clone();
    }
}
