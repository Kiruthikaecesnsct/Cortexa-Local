using System.Text.Json.Serialization;

namespace Collector.Domain.History;

public sealed record CandidateSource
{
    [JsonPropertyName("document_id")]
    public required string DocumentId { get; init; }

    [JsonPropertyName("page_number")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("line_start")]
    public int? LineStart { get; init; }

    [JsonPropertyName("line_end")]
    public int? LineEnd { get; init; }
}
