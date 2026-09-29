using System.Text.Json.Serialization;

namespace Cortexa.JobOrchestrator.Application.Contracts;

public sealed record EventEnvelope
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = "1.1";

    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("correlation_id")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("payload")]
    public Dictionary<string, object> Payload { get; init; } = [];
}
