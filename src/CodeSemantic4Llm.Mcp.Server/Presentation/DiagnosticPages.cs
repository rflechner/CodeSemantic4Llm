using CodeSemantic4Llm.Application.UseCases;
using CodeSemantic4Llm.Domain.Source;
using CodeSemantic4Llm.Domain.Symbols;
using ModelContextProtocol;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>
/// Retains bounded diagnostic continuation metadata without retaining source code.
/// A returned nextOffset identifies its own query and file version, so overlapping reads cannot mix pages.
/// </summary>
public sealed class DiagnosticPages
{
    private const int MaximumContinuations = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<long, DiagnosticContinuation> _continuations = [];
    private readonly Queue<long> _insertionOrder = [];
    // Keep continuation numbers exactly representable by JSON clients while separating server lifetimes.
    private long _nextId = Random.Shared.NextInt64(1L << 40, 1L << 50);

    internal DiagnosticContinuation Select(DiagnosticQuery query, long offset)
    {
        if (offset == 0)
        {
            return new DiagnosticContinuation(query, 0, null);
        }

        lock (_gate)
        {
            if (_continuations.TryGetValue(offset, out var continuation) && continuation.Query == query)
            {
                return continuation;
            }
        }

        throw new McpException("This diagnostics page is unavailable for the requested code. Start again at offset 0.");
    }

    internal CodeSourceDiagnosticsResult Present(DiagnosticContinuation continuation, SourceDiagnosticsResult result) =>
        new(result.RelativePath, result.TypeLocation, result.IsValid, result.Diagnostics, result.TotalDiagnostics,
            Continue(continuation, result.ContentHash, result.NextOffset));

    internal CodeMethodDiagnosticsResult Present(DiagnosticContinuation continuation, MethodDiagnosticsResult result) =>
        new(result.MethodLocation, result.IsValid, result.Diagnostics, result.TotalDiagnostics,
            Continue(continuation, result.ContentHash, result.NextOffset));

    private long? Continue(DiagnosticContinuation current, string contentHash, int? nextOffset)
    {
        if (current.ContentHash is not null && current.ContentHash != contentHash)
        {
            throw new McpException("The file changed while reading diagnostics. Start again at offset 0.");
        }

        if (nextOffset is not int offset)
        {
            return null;
        }

        lock (_gate)
        {
            if (_continuations.Count == MaximumContinuations)
            {
                _continuations.Remove(_insertionOrder.Dequeue());
            }

            var id = checked(++_nextId);
            _continuations.Add(id, new DiagnosticContinuation(current.Query, offset, contentHash));
            _insertionOrder.Enqueue(id);
            return id;
        }
    }
}

internal sealed record DiagnosticContinuation(DiagnosticQuery Query, int Offset, string? ContentHash);

internal abstract record DiagnosticQuery
{
    internal sealed record File(string RelativePath) : DiagnosticQuery;
    internal sealed record Type(SourceLocation Location, string Name) : DiagnosticQuery;
    internal sealed record Method(SourceLocation Location, string Name) : DiagnosticQuery;
    internal sealed record ProposedMethod(DeclarationSelector Selector, string BodyFingerprint) : DiagnosticQuery;
}

/// <summary>Current-source syntax feedback. NextOffset is a server-managed continuation, not a row number.</summary>
public sealed record CodeSourceDiagnosticsResult(
    string RelativePath, SourceLocation? TypeLocation, bool IsValid,
    IReadOnlyList<SyntaxDiagnostic> Diagnostics, int TotalDiagnostics, long? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}

/// <summary>Method syntax feedback without exposing file-version metadata.</summary>
public sealed record CodeMethodDiagnosticsResult(
    SourceLocation MethodLocation, bool IsValid,
    IReadOnlyList<SyntaxDiagnostic> Diagnostics, int TotalDiagnostics, long? NextOffset)
{
    public bool HasMore => NextOffset.HasValue;
}
