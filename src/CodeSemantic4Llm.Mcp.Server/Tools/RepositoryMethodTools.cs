using System.ComponentModel;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>Reads a selected C# method body from the checkout, without returning the whole file.</summary>
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

    private string GetRoot() => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new InvalidOperationException(
            "Pass the repository path to serve or set CODESEMANTIC4LLM_REPOSITORY_ROOT.");
}
