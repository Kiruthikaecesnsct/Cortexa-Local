namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IServiceBusStuckScanner
{
    Task<IReadOnlyCollection<string>> ListStuckBatchIdsAsync(CancellationToken ct);
}
