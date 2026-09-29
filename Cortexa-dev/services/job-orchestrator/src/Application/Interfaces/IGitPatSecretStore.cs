namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IGitPatSecretStore
{
    Task<string> StoreAsync(string batchId, string pat, CancellationToken ct);
}
