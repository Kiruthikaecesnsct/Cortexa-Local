using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Interfaces;

public interface IResultsReadRepository
{
    Task<HarvestingResultRecord?> GetHarvestingResultAsync(string batchId, CancellationToken ct);
    Task<SeedingReportRecord?> GetSeedingResultAsync(string batchId, CancellationToken ct);
    Task<CandidateResultDto?> GetCandidateDetailAsync(string batchId, string candidateId, CancellationToken ct);
}
