using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Snapshot-based adapter for direct callers; MCP hosts use RepositoryCodeTools instead.</summary>
[McpServerToolType]
public sealed class RepositoryMethodTools(IConfiguration configuration, MethodBodyOperations operations)
{
    [McpServerTool, Description("Reads the body of one exact indexed C# method from the selected checkout, with its source content hash. First use find_symbols or get_type_members to select its declaration and snapshotId. Results are paginated in characters; follow nextOffset. Requires the indexed file to be unchanged.")]
    public Task<MethodBodyReadResult> GetMethodBodyAsync(
        [Description("Indexed method declaration's repository-relative location.relativePath.")] string relativePath,
        [Description("One-based declaration location.line.")] int line,
        [Description("One-based declaration location.column.")] int column,
        [Description("snapshot.snapshotId from find_symbols.")] long snapshotId,
        [Description("Maximum characters to read, from 1 to 20000.")] int maxCharacters = 12000,
        [Description("Starting character offset, initially 0; follow nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.ReadAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, maxCharacters, offset, cancellationToken);

    [McpServerTool, Description("Reads one complete indexed C# method: attributes, signature and body, excluding surrounding comments. Call before replace_method; keep the returned contentHash and snapshotId. Follow nextOffset for more characters. Requires the indexed file to be unchanged.")]
    public Task<MethodDeclarationReadResult> GetMethodAsync(
        [Description("Indexed declaration's repository-relative source path.")] string relativePath,
        [Description("One-based identifier line.")] int line,
        [Description("One-based identifier column.")] int column,
        [Description("snapshot.snapshotId from find_symbols.")] long snapshotId,
        [Description("Maximum characters, 1–20000.")] int maxCharacters = 12000,
        [Description("Start at 0; follow nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.ReadDeclarationAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, maxCharacters, offset, cancellationToken);

    [McpServerTool, Description("Checks proposed C# body statements or a body block using Roslyn without writing. Do not send a method signature or complete declaration. Call get_method_body first and pass its declaration position, snapshotId and contentHash. Adds omitted outer braces and adjusts indentation. Reports syntax errors only; does not type-check or compile.")]
    public Task<MethodDiagnosticsResult> ValidateMethodBodyAsync(
        [Description("Method declaration's repository-relative location.relativePath.")] string relativePath,
        [Description("One-based method declaration location.line.")] int line,
        [Description("One-based method declaration location.column.")] int column,
        [Description("snapshot.snapshotId from get_method_body.")] long snapshotId,
        [Description("Exact contentHash returned by get_method_body.")] string expectedContentHash,
        [Description("Only C# statements or a body block, e.g. 'return 42;' or '{ return 42; }'. No method signature, modifiers or attributes.")] string newBody,
        [Description("Maximum diagnostics per page, from 1 to 200.")] int maxDiagnostics = 50,
        [Description("Start at 0; follow nextOffset to read subsequent diagnostics.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.ValidateAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            snapshotId, expectedContentHash, newBody, maxDiagnostics, offset, cancellationToken);

    [McpServerTool, Description("Returns Roslyn syntax errors and warnings inside an exact C# method CURRENTLY ON DISK, including immediately after editing it. Unlike get_method_body, this tool does NOT require a fresh SQLite index or snapshotId. Supply the CURRENT declaration line/column and expected method name. To find errors elsewhere in the file or when edits moved the declaration, use get_file_diagnostics. Checks only syntax, not types, imports or compilation; no writes.")]
    public Task<MethodDiagnosticsResult> GetMethodDiagnosticsAsync(
        [Description("Method source path relative to the operator-selected repository.")] string relativePath,
        [Description("One-based identifier line of the method, e.g. from find_symbols.")] int line,
        [Description("One-based identifier column of the method, e.g. from find_symbols.")] int column,
        [Description("Expected case-sensitive method name; prevents accidentally selecting a different declaration.")] string expectedMethodName,
        [Description("Maximum diagnostics per page, from 1 to 200.")] int maxDiagnostics = 50,
        [Description("Start at 0; follow nextOffset.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetDiagnosticsAsync(GetRoot(), new SourceLocation(relativePath, line, column),
            expectedMethodName, maxDiagnostics, offset, cancellationToken);

    private string GetRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException(
            "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT.");
}
