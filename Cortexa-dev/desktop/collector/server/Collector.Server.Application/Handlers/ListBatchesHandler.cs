using Collector.Domain.History;
using Collector.Server.Application.Ports;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Handlers;

public sealed class ListBatchesHandler(IPipelineRowStore store, BatchStageCalculator stageCalculator)
{
    public async Task<IReadOnlyList<BatchSummary>> HandleAsync(UploadCaller caller, CancellationToken cancellationToken)
    {
        var sagas = await store.ListSagasByOwnerAsync(caller.UserId, caller.OrgId, cancellationToken);
        return [.. sagas.Select(ToSummary)];
    }

    private BatchSummary ToSummary(SagaRow saga)
    {
        var stage = stageCalculator.Calculate(saga);
        return new BatchSummary
        {
            BatchId = saga.BatchId,
            BatchName = saga.BatchName,
            CreatedAt = saga.CreatedAt,
            State = saga.State,
            Stage = stage.Stage,
            ExtractionCompletedCount = saga.CompletedCount,
            ExtractionTotalCount = saga.TotalDocumentCount,
            EvidenceCompletedCount = saga.EvidenceCompletedCount,
            EmbeddingCompletedCount = saga.CompletedAssetEmbeddingUnits,
            EmbeddingTotalCount = saga.ExpectedAssetEmbeddingUnits,
            HarvestingCompletedCount = stage.HarvestingCompleted,
            HarvestingTotalCount = stage.HarvestingTotal,
            SeedingCompletedCount = stage.SeedingCompleted,
            SeedingTotalCount = stage.SeedingTotal,
            CollectorProvider = saga.CollectorProvider,
            CollectorModel = saga.CollectorModel
        };
    }
}
