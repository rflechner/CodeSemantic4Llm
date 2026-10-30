using System.Security.Cryptography;
using System.Text;
using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Symbols;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Presents source-addressed operations while keeping storage and concurrency metadata internal.</summary>
public sealed class CodeToolOperations(
    IConfiguration configuration,
    FindSymbols findSymbols,
    SelectCodeDeclaration selectDeclaration,
    GetTypeMembers getTypeMembers,
    FindUsages findUsages,
    MethodBodyOperations methodBodies,
    CurrentMethodOperations currentMethods,
    NavigationPages navigationPages,
    DiagnosticPages diagnosticPages,
    ILogger<CodeToolOperations> logger)
{
    public Task<CodeSymbolSearchResult> FindAsync(string name, int maxResults, CancellationToken cancellationToken) =>
        PresentAsync(async () =>
        {
            var result = await findSymbols.ExecuteAsync(Root, name, maxResults, cancellationToken);
            return new CodeSymbolSearchResult(result.Symbols, result.HasMore);
        });

    public Task<CodeTypeMemberPage> GetMembersAsync(
        DeclarationSelector selector, string? kind, int maxResults, long offset, CancellationToken cancellationToken) =>
        PresentAsync(async () =>
        {
            var selected = await selectDeclaration.ExecuteAsync(Root, selector, true, cancellationToken);
            var page = navigationPages.Select(new NavigationQuery(selector, true, kind), selected.Snapshot, offset);
            var result = await getTypeMembers.ExecuteAsync(Root, selected.Symbol.Location,
                selected.Snapshot.SnapshotId, kind, maxResults, page.Offset, cancellationToken);
            return new CodeTypeMemberPage(result.Type, result.Members, navigationPages.Continue(page, result.NextOffset));
        });

    public Task<CodeSymbolUsagePage> GetUsagesAsync(
        DeclarationSelector selector, int maxResults, long offset, CancellationToken cancellationToken) =>
        PresentAsync(async () =>
        {
            var selected = await selectDeclaration.ExecuteAsync(Root, selector, false, cancellationToken);
            var page = navigationPages.Select(new NavigationQuery(selector, false, null), selected.Snapshot, offset);
            var result = await findUsages.ExecuteAsync(Root, selected.Symbol.Location,
                selected.Snapshot.SnapshotId, maxResults, page.Offset, cancellationToken);
            return new CodeSymbolUsagePage(result.Target, result.Usages, navigationPages.Continue(page, result.NextOffset));
        });

    public Task<CurrentMethodBodyResult> ReadMethodAsync(
        DeclarationSelector selector, int maxCharacters, long offset, CancellationToken cancellationToken) =>
        PresentAsync(() => currentMethods.ReadAsync(Root, selector, maxCharacters, offset, cancellationToken));

    public Task<CodeMethodDiagnosticsResult> ValidateMethodAsync(
        DeclarationSelector selector, string newBody, int maxDiagnostics, long offset, CancellationToken cancellationToken) =>
        PresentAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(newBody);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(newBody)));
            var continuation = diagnosticPages.Select(new DiagnosticQuery.ProposedMethod(selector, fingerprint), offset);
            var result = await currentMethods.ValidateAsync(
                Root, selector, newBody, maxDiagnostics, continuation.Offset, cancellationToken);
            return diagnosticPages.Present(continuation, result);
        });

    public Task<CodeMethodWriteResult> ReplaceMethodAsync(
        DeclarationSelector selector, string newBody, CancellationToken cancellationToken) =>
        PresentAsync(async () => new CodeMethodWriteResult(
            await currentMethods.ReplaceAsync(Root, selector, newBody, cancellationToken)));

    public Task<CodeMethodDiagnosticsResult> GetMethodDiagnosticsAsync(
        SourceLocation location, string expectedMethodName, int maxDiagnostics, long offset,
        CancellationToken cancellationToken) =>
        PresentAsync(async () =>
        {
            var continuation = diagnosticPages.Select(new DiagnosticQuery.Method(location, expectedMethodName), offset);
            var result = await methodBodies.GetDiagnosticsAsync(
                Root, location, expectedMethodName, maxDiagnostics, continuation.Offset, cancellationToken);
            return diagnosticPages.Present(continuation, result);
        });

    private string Root => configuration["CODESEMANTIC4LLM_REPOSITORY_ROOT"]
        ?? throw new McpException("Ask the server operator to select a repository before using code tools.");

    private async Task<T> PresentAsync<T>(Func<Task<T>> operation)
    {
        try
        {
            return await operation();
        }
        catch (CodeRequestException exception)
        {
            throw new McpException(exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new McpException(exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or NotSupportedException)
        {
            logger.LogWarning(exception, "A code tool could not use the requested repository source information.");
            throw new McpException("Source information is unavailable. Check the source path or ask the server operator to refresh it.");
        }
    }
}

/// <summary>A bounded set of matching source declarations.</summary>
public sealed record CodeSymbolSearchResult(IReadOnlyList<SymbolDefinition> Symbols, bool HasMore);

/// <summary>A page of direct members of the selected type declaration.</summary>
public sealed record CodeTypeMemberPage(SymbolDefinition Type, IReadOnlyList<SymbolDefinition> Members, long? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}

/// <summary>A page of source references to the selected declaration.</summary>
public sealed record CodeSymbolUsagePage(SymbolDefinition Target, IReadOnlyList<SourceLocation> Usages, long? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}

/// <summary>The source location of a successfully edited method.</summary>
public sealed record CodeMethodWriteResult(SourceLocation MethodLocation);
