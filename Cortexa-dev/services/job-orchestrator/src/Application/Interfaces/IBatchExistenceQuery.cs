namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IBatchExistenceQuery
{
    Task<IReadOnlySet<string>> GetLiveBatchIdsAsync(CancellationToken ct);
}
