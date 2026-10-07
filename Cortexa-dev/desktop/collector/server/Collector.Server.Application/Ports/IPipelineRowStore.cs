using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Ports;

public interface IPipelineRowStore
{
    Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken);

    Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken);

    Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken);

    Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken);

    Task<SagaRow?> GetSagaAsync(string batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SagaRow>> ListSagasByOwnerAsync(string ownerUserId, string orgId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChunkRow>> GetChunksByBatchAsync(string batchId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BatchResultJoinRow>> GetResultsByBatchAsync(string batchId, CancellationToken cancellationToken);
}
