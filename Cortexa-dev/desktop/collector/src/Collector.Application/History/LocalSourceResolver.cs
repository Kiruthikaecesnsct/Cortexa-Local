using System.Collections.Concurrent;
using Collector.Application.Ports;
using Collector.Domain.Documents;
using Collector.Domain.Enums;
using Collector.Domain.Upload;

namespace Collector.Application.History;

public sealed class LocalSourceResolver(IBatchStore batchStore, IDocumentStore documentStore)
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, CollectorDocument>> _maps =
        new(StringComparer.Ordinal);

    public async Task<LocalSourceTarget?> ResolveAsync(
        string serverBatchId,
        string documentId,
        CancellationToken cancellationToken)
    {
        var map = await GetMapAsync(serverBatchId, cancellationToken);
        if (!map.TryGetValue(documentId, out var document) || document.SourceType != SourceType.Local)
        {
            return null;
        }

        return new LocalSourceTarget(document.SourcePath, document.SourceKind);
    }

    public void Invalidate(string serverBatchId) => _maps.TryRemove(serverBatchId, out _);

    private async Task<IReadOnlyDictionary<string, CollectorDocument>> GetMapAsync(
        string serverBatchId,
        CancellationToken cancellationToken)
    {
        if (_maps.TryGetValue(serverBatchId, out var cached))
        {
            return cached;
        }

        var built = await BuildMapAsync(serverBatchId, cancellationToken);
        return _maps.GetOrAdd(serverBatchId, built);
    }

    private async Task<IReadOnlyDictionary<string, CollectorDocument>> BuildMapAsync(
        string serverBatchId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, CollectorDocument>(StringComparer.Ordinal);
        var batch = await batchStore.FindByServerBatchIdAsync(serverBatchId, cancellationToken);
        if (batch is null)
        {
            return map;
        }

        foreach (var localId in batch.DocumentIds)
        {
            await AddDocumentAsync(map, serverBatchId, localId, cancellationToken);
        }

        return map;
    }

    private async Task AddDocumentAsync(
        Dictionary<string, CollectorDocument> map,
        string serverBatchId,
        string localId,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(localId, out var clientDocumentId))
        {
            return;
        }

        var document = await documentStore.GetAsync(localId, cancellationToken);
        if (document is not null)
        {
            map[DeterministicIds.DocumentId(serverBatchId, clientDocumentId)] = document;
        }
    }
}
