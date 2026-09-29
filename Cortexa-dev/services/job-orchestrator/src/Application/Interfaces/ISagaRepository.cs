using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface ISagaRepository
{
    Task<BatchSaga?> GetAsync(string batchId, CancellationToken ct);
    Task CreateAsync(BatchSaga saga, CancellationToken ct);
    Task UpdateAsync(BatchSaga saga, CancellationToken ct);
    Task DeleteAsync(string batchId, CancellationToken ct);
    Task<IReadOnlyList<BatchSaga>> ListAsync(CancellationToken ct);
    Task<IReadOnlyCollection<string>> ListStalledBatchIdsAsync(DateTimeOffset cutoffUtc, CancellationToken ct);
}
