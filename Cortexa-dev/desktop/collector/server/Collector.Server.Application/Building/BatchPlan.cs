using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public sealed record BatchPlan(
    IReadOnlyList<DocumentRow> Documents,
    IReadOnlyList<ChunkRow> Chunks,
    IReadOnlyList<ProvenanceRow> Provenance,
    SagaRow Saga);
