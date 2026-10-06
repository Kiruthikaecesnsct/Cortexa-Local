using System.Text.Json.Serialization;

namespace Collector.Server.Application.Events;

public sealed record IngestionCompletedPayload
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("blob_uri")]
    public string BlobUri { get; init; } = string.Empty;

    [JsonPropertyName("chunk_count")]
    public required int ChunkCount { get; init; }

    [JsonPropertyName("provenance_map_id")]
    public required string ProvenanceMapId { get; init; }
}
