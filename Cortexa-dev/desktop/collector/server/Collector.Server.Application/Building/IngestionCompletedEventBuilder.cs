using Collector.Server.Application.Events;
using Collector.Server.Application.Rows;

namespace Collector.Server.Application.Building;

public static class IngestionCompletedEventBuilder
{
    public static EventEnvelope Build(DocumentRow document, string correlationId, DateTimeOffset occurredAt) =>
        new()
        {
            EventId = Guid.NewGuid().ToString(),
            EventType = EventTypes.IngestionCompleted,
            BatchId = document.BatchId,
            DocumentId = document.Id,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            Payload = new IngestionCompletedPayload
            {
                DocumentId = document.Id,
                ChunkCount = document.ChunkCount,
                ProvenanceMapId = document.ProvenanceMapId
            }
        };
}
