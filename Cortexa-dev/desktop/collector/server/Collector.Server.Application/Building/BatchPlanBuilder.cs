using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public static class BatchPlanBuilder
{
    public static BatchPlan Build(WriteKnowledgeBatchCommand command, DateTimeOffset now)
    {
        var documents = new List<DocumentRow>(command.Documents.Count);
        var chunks = new List<ChunkRow>();
        var provenance = new List<ProvenanceRow>();

        foreach (var input in command.Documents)
        {
            var documentChunks = ChunkRowBuilder.Build(command.BatchId, input, command.Collector);
            documents.Add(DocumentRowBuilder.Build(command.BatchId, input, documentChunks.Count, now));
            chunks.AddRange(documentChunks);
            provenance.AddRange(ProvenanceRowBuilder.Build(documentChunks, input));
        }

        return new BatchPlan(documents, chunks, provenance, SagaRowBuilder.Build(command));
    }
}
