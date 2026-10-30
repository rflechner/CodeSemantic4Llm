using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Logging;

/// <summary>Correlates timings and exceptions; file-only message filters record parameters and replies.</summary>
internal static class McpToolCallLogging
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Wrap(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) => async (context, cancellationToken) =>
    {
        var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger(
            "CodeSemantic4Llm.Mcp.Server.ToolCalls") ?? NullLogger.Instance;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.JsonRpcRequest.Id.ToString(),
            ["ToolName"] = context.Params?.Name
        });
        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("MCP tool call started.");

        try
        {
            var result = await next(context, cancellationToken);
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (result.IsError == true)
            {
                logger.LogWarning("MCP tool call returned an error after {ElapsedMilliseconds:F1} ms.", elapsed);
            }
            else
            {
                logger.LogInformation("MCP tool call completed in {ElapsedMilliseconds:F1} ms.", elapsed);
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("MCP tool call canceled after {ElapsedMilliseconds:F1} ms.",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MCP tool call failed after {ElapsedMilliseconds:F1} ms.",
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    };
}
