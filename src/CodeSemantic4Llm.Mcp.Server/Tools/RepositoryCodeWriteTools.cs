using System.ComponentModel;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Presentation;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>The optional source writer for the default, code-focused MCP interface.</summary>
[McpServerToolType]
public sealed class RepositoryCodeWriteTools(CodeToolOperations operations)
{
    [McpServerTool, Description("Replaces only a C# method's body. Call get_method_body first. Rejects changed files and invalid blocks. Check the edited file with get_file_diagnostics.")]
    public Task<CodeMethodWriteResult> ReplaceMethodBodyAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified method name, e.g. CliApplication.RunAsync. Must be unambiguous.")] string name,
        [Description("C# body with or without outer braces, e.g. 'return 42;' or '{ return 42; }'. Omit the method signature.")] string newBody,
        [Description("Identifier line, only needed to disambiguate.")] int? line = null,
        [Description("Identifier column, only needed to disambiguate.")] int? column = null,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceMethodAsync(new DeclarationSelector(relativePath, name, line, column), newBody, cancellationToken);
}
