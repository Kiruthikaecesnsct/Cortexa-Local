using Microsoft.Extensions.Logging;

namespace Collector.Infrastructure.Remote;

internal sealed record RemotePage<T>(IReadOnlyList<T> Items, Uri? Next);

internal sealed class RemotePager(int maxPages, ILogger logger)
{
    public async Task<IReadOnlyList<T>> CollectAsync<T>(
        Uri first,
        Func<Uri, CancellationToken, Task<RemotePage<T>>> fetch,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        var next = first;
        for (var page = 0; next is not null; page++)
        {
            if (page >= maxPages)
            {
                logger.LogWarning("Pagination stopped after {MaxPages} pages; results may be incomplete.", maxPages);
                break;
            }

            var current = await fetch(next, cancellationToken);
            items.AddRange(current.Items);
            next = current.Next;
        }

        return items;
    }
}
