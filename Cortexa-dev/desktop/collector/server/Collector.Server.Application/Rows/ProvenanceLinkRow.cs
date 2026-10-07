using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record ProvenanceLinkRow
{
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("chunk_id")]
    public string? ChunkId { get; init; }

    [JsonPropertyName("source_chunk_index")]
    public int? SourceChunkIndex { get; init; }
}
