using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IRetentionSweeper
{
    Task<RetentionSweepReport> SweepAsync(bool? dryRunOverride, CancellationToken ct);
}
