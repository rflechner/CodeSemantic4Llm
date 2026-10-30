using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Provides syntax feedback from current files, including edits that invalidate indexed locations.</summary>
[McpServerToolType]
public sealed class RepositoryDiagnosticsTools(IConfiguration configuration, GetSourceDiagnostics operations)
{
    [McpServerTool, Description("Returns syntax errors and warnings in the whole C# file currently on disk. Use after editing or when a declaration cannot be selected. Follow nextOffset with contentHash as expectedContentHash. Does not write or type-check.")]
    public Task<SourceDiagnosticsResult> GetFileDiagnosticsAsync(
        [Description("C# file path relative to the operator-selected repository.")] string relativePath,
        [Description("Maximum diagnostics per page, from 1 to 200.")] int maxDiagnostics = 50,
        [Description("Start at 0; follow nextOffset until null.")] int offset = 0,
        [Description("Omit on the first page; required on subsequent pages: contentHash from the first response. Rejects concurrent file changes.")] string? expectedContentHash = null,
        CancellationToken cancellationToken = default) =>
        operations.GetFileAsync(GetRoot(), relativePath, maxDiagnostics, offset, expectedContentHash, cancellationToken);

    [McpServerTool, Description("Checks syntax within one C# type declaration, including members and nested types. Other partial parts are excluded. Supply the current identifier position and name; use get_file_diagnostics if the declaration moved. Follow nextOffset with contentHash as expectedContentHash.")]
    public Task<SourceDiagnosticsResult> GetTypeDiagnosticsAsync(
        [Description("Type source path relative to the operator-selected repository.")] string relativePath,
        [Description("One-based CURRENT type identifier line, e.g. from find_symbols if unchanged.")] int line,
        [Description("One-based CURRENT type identifier column.")] int column,
        [Description("Expected case-sensitive simple type name.")] string expectedTypeName,
        [Description("Maximum diagnostics per page, from 1 to 200.")] int maxDiagnostics = 50,
        [Description("Start at 0; follow nextOffset until null.")] int offset = 0,
        [Description("Omit on the first page; required on subsequent pages: contentHash from the first response.")] string? expectedContentHash = null,
        CancellationToken cancellationToken = default) =>
        operations.GetTypeAsync(GetRoot(), new SourceLocation(relativePath, line, column), expectedTypeName,
            maxDiagnostics, offset, expectedContentHash, cancellationToken);

    private string GetRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException(
            "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT.");
}
