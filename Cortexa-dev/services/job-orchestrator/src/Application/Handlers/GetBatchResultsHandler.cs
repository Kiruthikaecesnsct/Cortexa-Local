using Cortexa.JobOrchestrator.Application.Interfaces;
using Cortexa.JobOrchestrator.Application.Models;

namespace Cortexa.JobOrchestrator.Application.Handlers;

public sealed class GetBatchResultsHandler
{
    private readonly IResultsReadRepository _results;

    public GetBatchResultsHandler(IResultsReadRepository results)
    {
        _results = results;
    }

    public async Task<BatchResultsResponse> HandleAsync(string batchId, CancellationToken ct)
    {
        var harvestingTask = _results.GetHarvestingResultAsync(batchId, ct);
        var seedingTask = _results.GetSeedingResultAsync(batchId, ct);

        await Task.WhenAll(harvestingTask, seedingTask);

        return new BatchResultsResponse
        {
            Harvesting = await harvestingTask,
            Seeding = await seedingTask
        };
    }
}
