using System.Text.Json.Serialization;

namespace Collector.Server.Application.Events;

public sealed record EventEnvelope
{
    public const string CurrentSchemaVersion = "1.1";
    public const string CurrentEventVersion = "1.0";

    [JsonPropertyName("event_id")]
    public required string EventId { get; init; }

    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonPropertyName("event_version")]
    public string EventVersion { get; init; } = CurrentEventVersion;

    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("correlation_id")]
    public required string CorrelationId { get; init; }

    [JsonPropertyName("occurred_at")]
    public required DateTimeOffset OccurredAt { get; init; }

    [JsonPropertyName("payload")]
    public required IngestionCompletedPayload Payload { get; init; }
}
