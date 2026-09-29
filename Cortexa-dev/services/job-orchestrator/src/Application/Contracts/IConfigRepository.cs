using Cortexa.JobOrchestrator.Domain.Entities;

namespace Cortexa.JobOrchestrator.Application.Contracts;

public interface IConfigRepository
{
    Task<ModelConfig> GetAsync(CancellationToken ct);
    Task UpdateAsync(ModelConfig config, CancellationToken ct);
}
