using CodeSemantic4Llm.Domain.Repositories;
using CodeSemantic4Llm.Domain.Symbols;
using ModelContextProtocol;

namespace CodeSemantic4Llm.Mcp.Server.Presentation;

/// <summary>Prevents navigation pages from silently crossing source generations or queries.</summary>
public sealed class NavigationPages
{
    private const int MaximumPages = 4096;
    private readonly object _gate = new();
    private readonly Dictionary<long, NavigationPage> _pages = [];
    private readonly Queue<long> _order = [];
    private long _nextId = Random.Shared.NextInt64(1L << 40, 1L << 50);

    internal NavigationPage Select(NavigationQuery query, RepositoryIndexSnapshot snapshot, long offset)
    {
        if (offset == 0)
        {
            return new NavigationPage(query, snapshot, 0);
        }

        lock (_gate)
        {
            if (_pages.TryGetValue(offset, out var page) && page.Query == query && page.Snapshot == snapshot)
            {
                return page;
            }
        }

        throw new McpException("This results page is unavailable or the source changed. Start again at offset 0.");
    }

    internal long? Continue(NavigationPage page, int? nextOffset)
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
            _pages.Add(id, page with { Offset = offset });
            _order.Enqueue(id);
            return id;
        }
    }
}

internal sealed record NavigationQuery(DeclarationSelector Selector, bool Members, string? Kind);
internal sealed record NavigationPage(NavigationQuery Query, RepositoryIndexSnapshot Snapshot, int Offset);
