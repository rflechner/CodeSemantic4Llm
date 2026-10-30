using System.ComponentModel;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Mcp.Server.Presentation;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>The optional source writer for the default, code-focused MCP interface.</summary>
[McpServerToolType]
public sealed class RepositoryCodeWriteTools(CodeToolOperations operations)
{
    [McpServerTool, Description("Validates and replaces a C# method body in one call. No prior read is required. Send statements or a body block, NEVER a complete method declaration; use replace_method for signature changes. Invalid syntax returns up to 10 errors with IDs and proposed-file positions without writing. Correct and retry this tool. Adjusts indentation; rejects concurrent changes. Does not type-check.")]
    public Task<CodeMethodWriteResult> ReplaceMethodBodyAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified method name, e.g. CliApplication.RunAsync. Must be unambiguous.")] string name,
        [Description("Only statements, e.g. 'return 42;', or '{ return 42; }'. NO method name, return type, parameters, modifiers or attributes. Preserve relative indentation. Use replace_method for a complete method.")] string newBody,
        [Description("Identifier line, only needed to disambiguate.")] int? line = null,
        [Description("Identifier column, only needed to disambiguate.")] int? column = null,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceMethodBodyAsync(new DeclarationSelector(relativePath, name, line, column), newBody, cancellationToken);

    [McpServerTool, Description("Validates and replaces one current C# method: attributes, signature and body. No prior read is required. Select its current name and send the complete new declaration. Invalid syntax returns up to 10 errors with IDs and proposed-file positions without writing; correct and retry this tool. Preserves surrounding source and adjusts indentation. Rejects concurrent changes. Does not update callers or type-check. Returns the new identifier location.")]
    public Task<CodeMethodWriteResult> ReplaceMethodAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Current case-sensitive simple or qualified method name, before any rename. Must be unambiguous.")] string name,
        [Description("Exactly one complete C# method, e.g. 'public int Run(int value) { return value * 2; }'. Include every attribute to retain. No class, namespace, other members or surrounding comments. Preserve relative indentation.")] string newMethod,
        [Description("Current identifier line, only needed to disambiguate.")] int? line = null,
        [Description("Current identifier column, only needed to disambiguate.")] int? column = null,
        CancellationToken cancellationToken = default) =>
        operations.ReplaceMethodAsync(new DeclarationSelector(relativePath, name, line, column), newMethod, cancellationToken);
}
