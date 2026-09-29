using System.Text.Json.Serialization;

namespace Cortexa.JobOrchestrator.Infrastructure.Persistence;

internal sealed class SourceSpan
{
    [JsonPropertyName("source_kind")] public string? SourceKind { get; set; }
    [JsonPropertyName("locator")] public string? Locator { get; set; }
    [JsonPropertyName("section_hint")] public string? SectionHint { get; set; }
    [JsonPropertyName("span_start")] public int? SpanStart { get; set; }
    [JsonPropertyName("span_end")] public int? SpanEnd { get; set; }
    [JsonPropertyName("page_number")] public int? PageNumber { get; set; }
    [JsonPropertyName("excerpt")] public string? Excerpt { get; set; }
}

internal sealed class CandidateDocument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("abstract")] public string Abstract { get; set; } = string.Empty;
    [JsonPropertyName("claim_draft")] public string ClaimDraft { get; set; } = string.Empty;
    [JsonPropertyName("document_id")] public string DocumentId { get; set; } = string.Empty;
    [JsonPropertyName("source_chunk_index")] public int? SourceChunkIndex { get; set; }
    [JsonPropertyName("source_span")] public SourceSpan? SourceSpan { get; set; }
    [JsonPropertyName("source_filename")] public string? SourceFilename { get; set; }
}
