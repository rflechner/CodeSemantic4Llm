using CodeSemantic4Llm.Domain.Symbols;

namespace CodeSemantic4Llm.Application.UseCases;

/// <summary>Retains independent source observations for paged body reads without retaining their text.</summary>
public sealed class MethodBodyPages
{
    private const int MaximumPages = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<long, MethodBodyContinuation> _pages = [];
    private readonly Queue<long> _order = [];
    private long _nextId = Random.Shared.NextInt64(1L << 40, 1L << 50);

    internal MethodBodyContinuation Select(string root, DeclarationSelector selector, long offset)
    {
        if (offset == 0)
        {
            return new MethodBodyContinuation(root, selector, 0, null);
        }

        lock (_gate)
        {
            if (_pages.TryGetValue(offset, out var page) && page.Root == root && page.Selector == selector)
            {
                return page;
            }
        }

        throw new CodeRequestException("This method body page is unavailable. Start again at offset 0.");
    }

    internal long? Continue(MethodBodyContinuation page, MethodReadObservation observation, int? nextOffset)
    {
        if (nextOffset is not int offset)
        {
            return null;
        }

        lock (_gate)
        {
            if (_pages.Count == MaximumPages)
            {
                _pages.Remove(_order.Dequeue());
            }

            var id = checked(++_nextId);
            _pages.Add(id, page with { Offset = offset, Observation = observation });
            _order.Enqueue(id);
            return id;
        }
    }
}

internal sealed record MethodBodyContinuation(
    string Root, DeclarationSelector Selector, int Offset, MethodReadObservation? Observation);
