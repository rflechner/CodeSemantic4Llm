using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Bounds source-read observations independently of searches; retains no source text.</summary>
public sealed class MethodReadHistory
{
    private const int MaximumReads = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<ReadAddress, MethodReadObservation> _reads = [];
    private readonly Queue<ReadAddress> _order = [];

    public void Remember(MethodReadObservation observation)
    {
        var address = new ReadAddress(observation.State.Scope.RootPath, observation.Method.Location);
        lock (_gate)
        {
            if (!_reads.ContainsKey(address))
            {
                if (_reads.Count == MaximumReads)
                {
                    _reads.Remove(_order.Dequeue());
                }

                _order.Enqueue(address);
            }

            _reads[address] = observation;
        }
    }

    public MethodReadObservation Get(string root, SourceLocation location)
    {
        lock (_gate)
        {
            return _reads.TryGetValue(new ReadAddress(root, location), out var observation)
                ? observation
                : throw new CodeRequestException("Read this method with get_method_body before editing.");
        }
    }

    private sealed record ReadAddress(string Root, SourceLocation Location);
}

/// <summary>Server-side source provenance for the last successful read of a method.</summary>
public sealed record MethodReadObservation(RepositoryState State, SymbolDefinition Method, string ContentHash);
