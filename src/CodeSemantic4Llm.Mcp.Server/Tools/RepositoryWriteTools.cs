using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Registered as an MCP tool only when the operator starts serve with --allow-write.</summary>
[McpServerToolType]
public sealed class RepositoryWriteTools(IConfiguration configuration, MethodBodyOperations operations)
{
    [McpServerTool, Description("Replaces ONLY the executable body of one indexed C# method in the operator-selected checkout. This changes a real source file (not the SQLite index) and requires server startup with --allow-write. Call get_method_body first, pass its contentHash and the selected declaration's location/snapshotId, and supply a complete C# block including braces. Rejects stale indexes, stale file hashes, invalid syntax and methods without bodies. Does not build or execute code. Reindex the checkout after a successful edit.")]
    public Task<MethodBodyWriteResult> ReplaceMethodBodyAsync(
        [Description("Indexed method declaration's repository-relative location.relativePath.")] string relativePath,
        [Description("One-based declaration location.line.")] int line,
        [Description("One-based declaration location.column.")] int column,
        [Description("snapshot.snapshotId from find_symbols or get_method_body.")] long snapshotId,
        [Description("Exact contentHash returned by get_method_body.")] string expectedContentHash,
        [Description("Complete replacement C# body including '{' and '}', not an entire method declaration.")] string newBody,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, expectedContentHash, newBody, cancellationToken);

    private string GetRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException(
            "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT.");
}
