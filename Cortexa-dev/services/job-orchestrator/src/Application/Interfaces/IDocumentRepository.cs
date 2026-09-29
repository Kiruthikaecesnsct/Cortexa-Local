using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IDocumentRepository
{
    Task CreateManyAsync(IReadOnlyList<DocumentRecord> records, CancellationToken ct);
    Task<IReadOnlyList<DocumentRecord>> ListByBatchAsync(string batchId, CancellationToken ct);
    Task<DocumentRecord?> GetAsync(string batchId, string documentId, CancellationToken ct);
    Task UpdateStatusAsync(string batchId, string documentId, string status, CancellationToken ct);
}
