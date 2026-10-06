using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Ports;

public interface IPipelineRowStore
{
    Task UpsertDocumentsAsync(IReadOnlyList<DocumentRow> rows, CancellationToken cancellationToken);

    Task UpsertChunksAsync(IReadOnlyList<ChunkRow> rows, CancellationToken cancellationToken);

    Task UpsertProvenanceAsync(IReadOnlyList<ProvenanceRow> rows, CancellationToken cancellationToken);

    Task CreateSagaAsync(SagaRow saga, CancellationToken cancellationToken);
}
