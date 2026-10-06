using System.Text.Json.Serialization;

namespace Collector.Domain.Knowledge;

public sealed record KnowledgeSource
{
    [JsonPropertyName("page_number")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("section")]
    public string? Section { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("line_start")]
    public int? LineStart { get; init; }

    [JsonPropertyName("line_end")]
    public int? LineEnd { get; init; }
}
