using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Checks current code while keeping file-version tracking inside the server.</summary>
public sealed class CodeDiagnosticOperations(
    IConfiguration configuration, GetSourceDiagnostics diagnostics, DiagnosticPages pages,
    ILogger<CodeDiagnosticOperations> logger)
{
    public Task<CodeSourceDiagnosticsResult> GetFileAsync(
        string relativePath, int maxDiagnostics, long offset, CancellationToken cancellationToken)
    {
        var query = new DiagnosticQuery.File(relativePath.Replace('\\', '/'));
        return ReadAsync(query, offset, page => diagnostics.GetFileAsync(
            Root, relativePath, maxDiagnostics, page.Offset, page.ContentHash, cancellationToken));
    }

    public Task<CodeSourceDiagnosticsResult> GetTypeAsync(
        SourceLocation declaration, string expectedTypeName, int maxDiagnostics, long offset,
        CancellationToken cancellationToken) =>
        ReadAsync(new DiagnosticQuery.Type(declaration, expectedTypeName), offset,
            page => diagnostics.GetTypeAsync(Root, declaration, expectedTypeName,
                maxDiagnostics, page.Offset, page.ContentHash, cancellationToken));

    private string Root => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new McpException("Ask the server operator to select a repository before using code tools.");

    private async Task<CodeSourceDiagnosticsResult> ReadAsync(
        DiagnosticQuery query, long offset, Func<DiagnosticContinuation, Task<SourceDiagnosticsResult>> read)
    {
        var continuation = pages.Select(query, offset);

        try
        {
            return pages.Present(continuation, await read(continuation));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Current source diagnostics could not complete.");
            throw new McpException(exception.Message);
        }
    }
}
