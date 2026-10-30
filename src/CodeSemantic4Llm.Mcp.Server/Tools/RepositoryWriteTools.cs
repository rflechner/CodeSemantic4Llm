using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Snapshot-based adapter for direct callers; MCP hosts use RepositoryCodeWriteTools instead.</summary>
[McpServerToolType]
public sealed class RepositoryWriteTools(IConfiguration configuration, MethodBodyOperations operations)
{
    [McpServerTool, Description("Changes ONLY the executable body of one indexed C# method; its signature and attributes stay unchanged. Send statements or a body block, NEVER a complete method declaration. For signature changes use replace_method. Requires --allow-write. Call get_method_body first and pass its contentHash and declaration location/snapshotId. Rejects stale indexes, changed files and invalid syntax. Adjusts indentation. Reindex after editing.")]
    public Task<MethodBodyWriteResult> ReplaceMethodBodyAsync(
        [Description("Indexed method declaration's repository-relative location.relativePath.")] string relativePath,
        [Description("One-based declaration location.line.")] int line,
        [Description("One-based declaration location.column.")] int column,
        [Description("snapshot.snapshotId from find_symbols or get_method_body.")] long snapshotId,
        [Description("Exact contentHash returned by get_method_body.")] string expectedContentHash,
        [Description("Only statements, e.g. 'return 42;', or '{ return 42; }'. NO method name, return type, parameters, modifiers or attributes. Preserve relative indentation. Use replace_method for a complete method.")] string newBody,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, expectedContentHash, newBody, cancellationToken);

    [McpServerTool, Description("Replaces one complete indexed C# method: attributes, signature and body. Call get_method first and pass its OLD declaration location, snapshotId and contentHash. Requires --allow-write; rejects stale source and invalid syntax. Preserves surrounding comments and other members; adjusts indentation. Does not update callers or type-check. Returns the NEW identifier location. Check get_file_diagnostics, affected callers and reindex after editing.")]
    public Task<MethodBodyWriteResult> ReplaceMethodAsync(
        [Description("Current method's repository-relative source path.")] string relativePath,
        [Description("Current identifier line, before the edit.")] int line,
        [Description("Current identifier column, before the edit.")] int column,
        [Description("snapshot.snapshotId from get_method.")] long snapshotId,
        [Description("Exact contentHash from get_method.")] string expectedContentHash,
        [Description("Exactly one complete C# method, e.g. 'public int Run(int value) { return value * 2; }'. Include every attribute to retain. No class, namespace, other members or surrounding comments. Preserve relative indentation.")] string newMethod,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceDeclarationAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, expectedContentHash, newMethod, cancellationToken);

    private string GetRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException(
            "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT.");
}
