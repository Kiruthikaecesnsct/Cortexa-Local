using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Contracts;

public interface IPatentSourceConfigRepository
{
    Task<PatentSourceConfig> GetAsync(CancellationToken ct);

    Task<PatentSourceConfig> UpdateAsync(PatentSourceConfig desired, CancellationToken ct);
}
