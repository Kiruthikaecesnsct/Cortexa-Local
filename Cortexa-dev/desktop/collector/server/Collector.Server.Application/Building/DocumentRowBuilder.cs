using Collector.Server.Application.Commands;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public static class DocumentRowBuilder
{
    public static string BuildProvenanceMapId(string batchId, string documentId) =>
        $"{batchId}:{documentId}";

    public static DocumentRow Build(string batchId, BatchDocumentInput document, int chunkCount, DateTimeOffset createdAt) =>
        new()
        {
            Id = document.DocumentId,
            BatchId = batchId,
            Filename = document.Filename,
            SourceKind = document.SourceKind,
            ProvenanceMapId = BuildProvenanceMapId(batchId, document.DocumentId),
            ChunkCount = chunkCount,
            CreatedAt = createdAt
        };
}
