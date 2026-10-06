using System.Text.Json.Serialization;

namespace Collector.Server.Application.Rows;

public sealed record ChunkRow : IPipelineRow
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("batch_id")]
    public required string BatchId { get; init; }

    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("order_index")]
    public required int OrderIndex { get; init; }

    [JsonPropertyName("start_char")]
    public required int StartChar { get; init; }

    [JsonPropertyName("end_char")]
    public required int EndChar { get; init; }

    [JsonPropertyName("token_count")]
    public required int TokenCount { get; init; }

    [JsonPropertyName("page_number")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("section_hint")]
    public string? SectionHint { get; init; }

    [JsonPropertyName("knowledge")]
    public required ChunkKnowledge Knowledge { get; init; }
}
