using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class GetResultDetailHandler
{
    private readonly IResultsReadRepository _results;

    public GetResultDetailHandler(IResultsReadRepository results)
    {
        _results = results;
    }

    public Task<CandidateResultDto?> HandleAsync(string batchId, string candidateId, CancellationToken ct)
        => _results.GetCandidateDetailAsync(batchId, candidateId, ct);
}
