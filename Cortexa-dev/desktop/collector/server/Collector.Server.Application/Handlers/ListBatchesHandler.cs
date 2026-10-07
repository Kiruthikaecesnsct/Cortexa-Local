using Collector.Server.Application.Ports;
using Collector.Server.Application.Reads;
using Collector.Server.Application.Rows;
using Collector.Server.Application.Upload;

namespace Collector.Server.Application.Handlers;

public sealed class ListBatchesHandler(IPipelineRowStore store)
{
    public async Task<IReadOnlyList<BatchSummaryDto>> HandleAsync(UploadCaller caller, CancellationToken cancellationToken)
    {
        var sagas = await store.ListSagasByOwnerAsync(caller.UserId, caller.OrgId, cancellationToken);
        return [.. sagas.Select(ToSummary)];
    }

    private static BatchSummaryDto ToSummary(SagaRow saga) => new()
    {
        BatchId = saga.BatchId,
        BatchName = saga.BatchName,
        CreatedAt = saga.CreatedAt,
        State = saga.State,
        ExtractionCompletedCount = saga.CompletedCount,
        ExtractionTotalCount = saga.TotalDocumentCount,
        EvidenceCompletedCount = saga.EvidenceCompletedCount,
        EmbeddingCompletedCount = saga.CompletedAssetEmbeddingUnits,
        EmbeddingTotalCount = saga.ExpectedAssetEmbeddingUnits
    };
}
