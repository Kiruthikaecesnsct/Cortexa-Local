using System.Text.Json.Serialization;
using Collector.Domain.Enums;

namespace Collector.Server.Application.Rows;

public sealed record ProvenanceRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("chunk_id")]
    public required string ChunkId { get; init; }

    [JsonPropertyName("source_kind")]
    public required SourceKind SourceKind { get; init; }

    [JsonPropertyName("order_index")]
    public required int OrderIndex { get; init; }

    [JsonPropertyName("doc_id")]
    public required string DocId { get; init; }

    [JsonPropertyName("byte_range")]
    public IReadOnlyList<int>? ByteRange { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("line_range")]
    public IReadOnlyList<int>? LineRange { get; init; }
}
