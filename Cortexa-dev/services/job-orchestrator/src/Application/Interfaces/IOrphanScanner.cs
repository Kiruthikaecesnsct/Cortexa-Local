namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IOrphanScanner
{
    string StoreName { get; }
    Task<IReadOnlyCollection<string>> ListBatchIdsAsync(CancellationToken ct);
}
