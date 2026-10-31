using System.ComponentModel;
using CodeSemantic4Llm.Domain.Symbols;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Mcp.Server.Presentation;
using ModelContextProtocol.Server;

namespace CodeSemantic4Llm.Mcp.Server.Tools;

/// <summary>The default code-focused MCP interface; storage provenance stays inside the server.</summary>
[McpServerToolType]
public sealed class RepositoryCodeTools(CodeToolOperations operations)
{
    [McpServerTool, Description("Finds declarations by exact, case-sensitive simple name. Use the returned file path and name to select a declaration; add its line and column when ambiguous.")]
    public Task<CodeSymbolSearchResult> FindSymbolsAsync(
        string name, [Description("Maximum results, 1–200.")] int maxResults = 20,
        CancellationToken cancellationToken = default) =>
        operations.FindAsync(name, maxResults, cancellationToken);

    [McpServerTool, Description("Searches saved declaration names and optional LLM-generated summaries by keywords or natural-language intent using local SQLite FTS5/BM25. Results are ranked and contain locations and concise descriptions, not source code. This is lexical, not vector similarity. No LLM calls occur during search.")]
    public Task<CodeSymbolSearchResult> SearchCodeAsync(
        [Description("Plain-language keywords or question about what code does.")] string query,
        [Description("Optional declaration kind, such as method, class or interface.")] string? kind = null,
        [Description("Maximum ranked results, 1–200.")] int maxResults = 10,
        CancellationToken cancellationToken = default) =>
        operations.SearchAsync(query, kind, maxResults, cancellationToken);

    [McpServerTool, Description("Lists direct members of a type. Inherited members and other partial declarations are excluded. Follow nextOffset for more.")]
    public Task<CodeTypeMemberPage> GetTypeMembersAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified type name; leading qualifiers may be omitted when unambiguous.")] string name,
        [Description("Optional member kind, such as method, constructor, property or field.")] string? kind = null,
        [Description("Identifier line, only needed to disambiguate.")] int? line = null,
        [Description("Identifier column, only needed to disambiguate.")] int? column = null,
        [Description("Maximum results, 1–200.")] int maxResults = 50,
        [Description("Start at 0; pass nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetMembersAsync(new DeclarationSelector(relativePath, name, line, column), kind, maxResults, offset, cancellationToken);

    [McpServerTool, Description("Finds resolved source references to a method or type. Unresolved references are omitted; C# project references are not yet evaluated. Follow nextOffset for more.")]
    public Task<CodeSymbolUsagePage> FindUsagesAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified declaration name; leading qualifiers may be omitted when unambiguous.")] string name,
        [Description("One-based identifier line, only needed to disambiguate.")] int? line = null,
        [Description("One-based identifier column, only needed to disambiguate.")] int? column = null,
        [Description("Maximum results, 1–200.")] int maxResults = 50,
        [Description("Start at 0; pass nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetUsagesAsync(new DeclarationSelector(relativePath, name, line, column), maxResults, offset, cancellationToken);

    [McpServerTool, Description("Reads one C# method body. Follow nextOffset for more characters.")]
    public Task<CurrentMethodBodyResult> GetMethodBodyAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified method name, e.g. CliApplication.RunAsync. Must be unambiguous.")] string name,
        [Description("One-based identifier line, only needed to disambiguate.")] int? line = null,
        [Description("One-based identifier column, only needed to disambiguate.")] int? column = null,
        [Description("Maximum characters, 1–20000.")] int maxCharacters = 12000,
        [Description("Start at 0; pass nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.ReadMethodAsync(new DeclarationSelector(relativePath, name, line, column), maxCharacters, offset, cancellationToken);

    [McpServerTool, Description("Checks a proposed C# method body without writing. Adds omitted outer braces. Returns syntax diagnostics, not type or compilation errors. Follow nextOffset for more.")]
    public Task<CodeMethodDiagnosticsResult> ValidateMethodBodyAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("Case-sensitive simple or qualified method name, e.g. CliApplication.RunAsync. Must be unambiguous.")] string name,
        [Description("C# body with or without outer braces, e.g. 'return 42;' or '{ return 42; }'. Omit the method signature.")] string newBody,
        [Description("One-based identifier line, only needed to disambiguate.")] int? line = null,
        [Description("One-based identifier column, only needed to disambiguate.")] int? column = null,
        [Description("Maximum diagnostics, 1–200.")] int maxDiagnostics = 50,
        [Description("Start at 0; then pass the returned nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.ValidateMethodAsync(new DeclarationSelector(relativePath, name, line, column), newBody, maxDiagnostics, offset, cancellationToken);

    [McpServerTool, Description("Checks syntax of a C# method currently on disk. Supply its current identifier position and name. Use get_file_diagnostics if the declaration moved or errors may be elsewhere.")]
    public Task<CodeMethodDiagnosticsResult> GetMethodDiagnosticsAsync(
        [Description("Repository-relative source path.")] string relativePath,
        [Description("One-based identifier line.")] int line,
        [Description("One-based identifier column.")] int column,
        [Description("Case-sensitive method name.")] string expectedMethodName,
        [Description("Maximum diagnostics, 1–200.")] int maxDiagnostics = 50,
        [Description("Start at 0; then pass the returned nextOffset unchanged.")] long offset = 0,
        CancellationToken cancellationToken = default) =>
        operations.GetMethodDiagnosticsAsync(new SourceLocation(relativePath, line, column),
            expectedMethodName, maxDiagnostics, offset, cancellationToken);
}
