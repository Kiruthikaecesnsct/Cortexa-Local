using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IReconciliationHandler
{
    Task<ReconciliationReport> ScanAsync(CancellationToken ct);
    Task<ReconciliationReport> ReconcileAsync(bool confirmed, CancellationToken ct);
}
