using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Logging;

/// <summary>
/// Records tool parameters and actual outgoing replies only in the operator-selected file.
/// Message filters also see error replies produced by the SDK after a tool throws.
/// </summary>
internal sealed class McpToolPayloadLogging(FileLoggerProvider provider)
{
    private readonly ConcurrentDictionary<RequestId, PendingCall> _pendingCalls = new();

    public McpMessageHandler WrapIncoming(McpMessageHandler next) => async (context, cancellationToken) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest { Method: "tools/call" } request)
        {
            var name = request.Params is JsonObject parameters
                && parameters["name"] is JsonValue value && value.TryGetValue<string>(out var text)
                ? text : null;
            _pendingCalls[request.Id] = new PendingCall(name);
            provider.WriteToolPayload(LogLevel.Information, "MCP tool call parameters received.",
                new ToolCallPayload(request.Id.ToString(), name, Parameters: request.Params));
        }

        await next(context, cancellationToken);
    };

    public McpMessageHandler WrapOutgoing(McpMessageHandler next) => async (context, cancellationToken) =>
    {
        RequestId? id = context.JsonRpcMessage switch
        {
            JsonRpcResponse response => response.Id,
            JsonRpcError error => error.Id,
            _ => null
        };

        if (id is { } requestId && _pendingCalls.TryRemove(requestId, out var call))
        {
            var response = JsonSerializer.SerializeToNode(context.JsonRpcMessage, McpJsonUtilities.DefaultOptions);
            provider.WriteToolPayload(LogLevel.Information, "MCP tool call response prepared.",
                new ToolCallPayload(requestId.ToString(), call.Name, Response: response));
        }

        await next(context, cancellationToken);
    };

    private sealed record PendingCall(string? Name);
}

/// <summary>Explicit payload fields; unrelated logger scopes are never copied into the file.</summary>
internal sealed record ToolCallPayload(
    string RequestId, string? ToolName, JsonNode? Parameters = null, JsonNode? Response = null);
