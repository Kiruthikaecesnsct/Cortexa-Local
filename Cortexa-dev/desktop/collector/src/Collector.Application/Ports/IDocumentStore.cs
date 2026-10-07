using Collector.Domain.Documents;
using Collector.Domain.Enums;

namespace Collector.Application.Ports;

public interface IDocumentStore
{
    Task<CollectorDocument> UpsertAsync(
        SourceType sourceType,
        SourceKind sourceKind,
        string sourcePath,
        string filename,
        string contentHash,
        long sizeBytes,
        CancellationToken cancellationToken);

    Task UpdateStatusAsync(string documentId, DocumentStatus status, CancellationToken cancellationToken);
}
