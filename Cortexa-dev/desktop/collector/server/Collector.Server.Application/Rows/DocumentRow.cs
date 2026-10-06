using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Server.Application.Rows;

public sealed record DocumentRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("blob_uri")]
    public string BlobUri { get; init; } = string.Empty;

    [JsonPropertyName("filename")]
    public required string Filename { get; init; }

    [JsonPropertyName("source_kind")]
    public required SourceKind SourceKind { get; init; }

    [JsonPropertyName("provenance_map_id")]
    public required string ProvenanceMapId { get; init; }

    [JsonPropertyName("chunk_count")]
    public required int ChunkCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = RowConstants.DocumentStatusCompleted;

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }
}
