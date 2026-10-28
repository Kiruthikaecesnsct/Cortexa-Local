using Collector.Application.History;

namespace Collector.Application.Ports;

public interface IProcessedDocumentRepository
{
    Task<IReadOnlyList<ProcessedDocumentRecord>> ListAsync(string userId, CancellationToken cancellationToken);

    Task UpsertAsync(ProcessedDocumentRecord record, CancellationToken cancellationToken);
}

public sealed class ProcessedDocumentStoreException(string message, Exception? innerException = null)
    : Exception(message, innerException);
