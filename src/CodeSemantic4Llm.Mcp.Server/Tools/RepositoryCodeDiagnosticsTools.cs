using System.ComponentModel;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Presentation;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Current-file syntax checks with no client-managed version metadata.</summary>
[McpServerToolType]
public sealed class RepositoryCodeDiagnosticsTools(CodeDiagnosticOperations operations)
{
    [McpServerTool, Description("Returns syntax errors and warnings in the whole C# file currently on disk. Use after editing or when a declaration cannot be selected. Follow nextOffset for more. Does not write or type-check.")]
    public Task<CodeSourceDiagnosticsResult> GetFileDiagnosticsAsync(
        [Description("Repository-relative C# source path.")] string relativePath,
        [Description("Maximum diagnostics, 1–200.")] int maxDiagnostics = 50,
        [Description("Start at 0; then pass the returned nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetFileAsync(relativePath, maxDiagnostics, offset, cancellationToken);

    [McpServerTool, Description("Checks syntax within one C# type declaration, including its members and nested types. Other partial parts are excluded. Supply the current identifier position and name; use get_file_diagnostics if it moved. Follow nextOffset for more.")]
    public Task<CodeSourceDiagnosticsResult> GetTypeDiagnosticsAsync(
        [Description("Repository-relative C# source path.")] string relativePath,
        [Description("One-based type identifier line.")] int line,
        [Description("One-based type identifier column.")] int column,
        [Description("Case-sensitive type name.")] string expectedTypeName,
        [Description("Maximum diagnostics, 1–200.")] int maxDiagnostics = 50,
        [Description("Start at 0; then pass the returned nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetTypeAsync(new SourceLocation(relativePath, line, column), expectedTypeName,
            maxDiagnostics, offset, cancellationToken);
}
